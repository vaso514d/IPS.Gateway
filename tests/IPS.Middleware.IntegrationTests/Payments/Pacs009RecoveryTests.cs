using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Investigation;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Payments;

// An unknown outcome of a pacs.009 is resolved by resending the exact original, flagged as a possible duplicate.
[Collection("Investigation SQL timing")]
public sealed class Pacs009RecoveryTests
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(9);

    [Fact]
    public async Task Due_after_the_first_delay_a_flagged_resend_of_the_original_bytes_settles_the_payment()
    {
        await using var h = await Harness.CreateAsync();
        Assert.Empty(await h.DueAsync());
        h.Core.Clock.Now += FirstDelay - TimeSpan.FromTicks(1);
        Assert.Empty(await h.DueAsync());
        h.Core.Clock.Now += TimeSpan.FromTicks(1);
        Assert.Contains(h.Id, await h.DueAsync());

        var outcome = (await h.RunAsync())!;

        Assert.Equal(TransactionStatus.Accepted, outcome.Status);
        Assert.Equal(StatusSource.Ips, outcome.Source);
        var resent = Assert.Single(h.Core.Ips.Resent);
        Assert.Equal(h.Core.Ips.Received[0], resent);
        Assert.Equal(2, h.Core.Ips.Received.Count);
        var attempt = await h.ReadAttemptAsync();
        Assert.Equal(1, attempt.Number);
        Assert.Null(attempt.InvestigationId);
        Assert.Equal(ProcessingHarness.Start + TimeSpan.FromHours(24), attempt.DeadlineUtc);
        Assert.Equal(IpsReplyStatus.Accepted, attempt.Result!.Status);
        Assert.Equal(MessageJournalStatus.Processed, attempt.Response!.Status);
        Assert.Equal(1, await h.CallbacksAsync());
    }

    [Fact]
    public async Task A_lost_reply_schedules_the_next_attempt_on_the_backoff_and_never_repeats_a_marker()
    {
        await using var h = await Harness.CreateAsync();
        h.Core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost", []));
        h.Core.Clock.Now += FirstDelay;

        Assert.Equal(TransactionStatus.Uncertain, (await h.RunAsync())!.Status);
        Assert.Equal(h.Core.Clock.Now + TimeSpan.FromSeconds(30), (await h.Core.ReadAsync(h.Id)).NextActionAtUtc);

        // Before the backoff elapses nothing is claimed or sent.
        h.Core.Clock.Now += TimeSpan.FromSeconds(30) - TimeSpan.FromTicks(1);
        Assert.Empty(await h.DueAsync());
        await h.RunAsync();
        Assert.Single(h.Core.Ips.Resent);

        h.Core.Clock.Now += TimeSpan.FromTicks(1);
        Assert.Equal(TransactionStatus.Uncertain, (await h.RunAsync())!.Status);
        Assert.Equal(2, h.Core.Ips.Resent.Count);
        Assert.Equal(h.Core.Clock.Now + TimeSpan.FromMinutes(1), (await h.Core.ReadAsync(h.Id)).NextActionAtUtc);

        h.Core.Ips.Behavior = null;
        h.Core.Clock.Now += TimeSpan.FromMinutes(1);
        Assert.Equal(TransactionStatus.Accepted, (await h.RunAsync())!.Status);
        Assert.Equal(3, h.Core.Ips.Resent.Count);
        Assert.All(h.Core.Ips.Resent, xml => Assert.Equal(h.Core.Ips.Received[0], xml));
        Assert.Equal(3, (await h.ReadAttemptAsync()).Number);
        Assert.Equal(1, await h.CallbacksAsync());
    }

    [Fact]
    public async Task The_attempt_limit_goes_to_manual_review_with_a_callback()
    {
        await using var h = await Harness.CreateAsync();
        h.Options = new(maxCycles: 2);
        h.Core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost", []));
        h.Core.Clock.Now += FirstDelay;
        await h.RunAsync();
        h.Core.Clock.Now += TimeSpan.FromSeconds(30);
        await h.RunAsync();
        h.Core.Clock.Now += TimeSpan.FromMinutes(1);

        Assert.Equal(TransactionStatus.ManualReview, (await h.RunAsync())!.Status);

        Assert.Equal(2, h.Core.Ips.Resent.Count);
        Assert.Equal(1, await h.CallbacksAsync());
    }

    [Fact]
    public async Task The_exact_window_end_prevents_a_resend_and_requires_manual_review()
    {
        await using var h = await Harness.CreateAsync();
        h.Core.Clock.Now = ProcessingHarness.Start + TimeSpan.FromHours(24);

        Assert.Equal(TransactionStatus.ManualReview, (await h.RunAsync())!.Status);

        Assert.Empty(h.Core.Ips.Resent);
        Assert.Equal(1, await h.CallbacksAsync());
    }

    [Fact]
    public async Task A_marked_attempt_without_a_reply_is_abandoned_then_retried_as_a_new_attempt()
    {
        await using var h = await Harness.CreateAsync();
        h.Core.Clock.Now += FirstDelay;
        using var cancelled = new CancellationTokenSource();
        h.Core.Ips.Behavior = (_, _) => { cancelled.Cancel(); throw new OperationCanceledException(cancelled.Token); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.RunAsync(token: cancelled.Token));
        h.Core.Ips.Behavior = null;

        // The committed marker is never sent again; the next owner records it as abandoned.
        h.Core.Clock.Now += h.Options.Ownership;
        await h.Core.RecoverAsync(h.Id);
        var abandoned = (await h.RunAsync())!;
        Assert.Equal(TransactionStatus.Uncertain, abandoned.Status);
        Assert.Single(h.Core.Ips.Resent);
        var first = await h.ReadAttemptAsync();
        Assert.Contains("Abandoned", first.TransportFailure);
        Assert.Equal(h.Core.Clock.Now + TimeSpan.FromSeconds(30), (await h.Core.ReadAsync(h.Id)).NextActionAtUtc);

        h.Core.Clock.Now += TimeSpan.FromSeconds(30);
        Assert.Equal(TransactionStatus.Accepted, (await h.RunAsync())!.Status);
        Assert.Equal(2, h.Core.Ips.Resent.Count);
        Assert.Equal(2, (await h.ReadAttemptAsync()).Number);
    }

    [Fact]
    public async Task A_saved_reply_is_interpreted_after_the_window_without_another_send()
    {
        await using var h = await Harness.CreateAsync();
        h.Core.Clock.Now += FirstDelay;
        await Assert.ThrowsAsync<SimulatedCrash>(() => h.RunAsync(new CrashWhenResultIsSaved()));
        Assert.Single(h.Core.Ips.Resent);

        h.Core.Clock.Now = ProcessingHarness.Start + TimeSpan.FromHours(25);
        await h.Core.RecoverAsync(h.Id);
        Assert.Equal(TransactionStatus.Accepted, (await h.RunAsync())!.Status);
        Assert.Single(h.Core.Ips.Resent);
    }

    [Fact]
    public async Task A_competing_owner_cannot_send_the_attempt_twice()
    {
        await using var h = await Harness.CreateAsync();
        h.Core.Clock.Now += FirstDelay;
        h.Core.Ips.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = h.RunAsync();
        await WaitUntilAsync(() => h.Core.Ips.Resent.Count == 1);

        Assert.Equal(TransactionStatus.Resending, (await h.RunAsync())!.Status);
        Assert.Single(h.Core.Ips.Resent);

        h.Core.Ips.Gate.SetResult();
        Assert.Equal(TransactionStatus.Accepted, (await first)!.Status);
        Assert.Single(h.Core.Ips.Resent);
    }

    [Fact]
    public async Task Backoff_follows_the_schedule_then_repeats_every_fifteen_minutes()
    {
        await using var h = await Harness.CreateAsync();
        h.Core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost", []));
        h.Core.Clock.Now += FirstDelay;
        TimeSpan[] schedule = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15)];

        foreach (var delay in schedule)
        {
            await h.RunAsync();
            Assert.Equal(h.Core.Clock.Now + delay, (await h.Core.ReadAsync(h.Id)).NextActionAtUtc);
            h.Core.Clock.Now += delay;
        }

        Assert.Equal(schedule.Length, h.Core.Ips.Resent.Count);
    }

    [Fact]
    public async Task The_last_call_is_clamped_to_the_remaining_window()
    {
        await using var h = await Harness.CreateAsync();
        var deadline = ProcessingHarness.Start + TimeSpan.FromHours(24);
        h.Core.Clock.Now = deadline - TimeSpan.FromMilliseconds(100);
        h.Core.Ips.Behavior = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new IpsSubmissionResponse(200, "", []);
        };

        Assert.Equal(TransactionStatus.Uncertain, (await h.RunAsync().WaitAsync(TimeSpan.FromSeconds(10)))!.Status);

        var attempt = await h.ReadAttemptAsync();
        Assert.NotNull(attempt.TransportFailure);
        Assert.Null(attempt.Response);
        Assert.Equal(deadline, (await h.Core.ReadAsync(h.Id)).NextActionAtUtc);
        h.Core.Clock.Now = deadline;
        Assert.Equal(TransactionStatus.ManualReview, (await h.RunAsync())!.Status);
        Assert.Single(h.Core.Ips.Resent);
    }

    [Fact]
    public async Task A_refused_disposition_waits_inside_the_window_and_ends_in_manual_review_at_its_end()
    {
        await using var h = await Harness.CreateAsync();
        h.Protocol = new RefusingProtocol();
        h.Core.Clock.Now += FirstDelay;

        Assert.Equal(TransactionStatus.Resending, (await h.RunAsync())!.Status);
        Assert.Equal(h.Core.Clock.Now + h.Options.PreparationRetryDelay, (await h.Core.ReadAsync(h.Id)).NextActionAtUtc);
        Assert.Empty(h.Core.Ips.Resent);

        h.Core.Clock.Now = ProcessingHarness.Start + TimeSpan.FromHours(24);
        Assert.Equal(TransactionStatus.ManualReview, (await h.RunAsync())!.Status);
        Assert.Empty(h.Core.Ips.Resent);
    }

    [Fact]
    public async Task A_stale_owner_cannot_store_a_reply_for_an_attempt_the_next_owner_abandoned()
    {
        await using var h = await Harness.CreateAsync();
        h.Core.Clock.Now += FirstDelay;
        h.Core.Ips.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var stale = h.RunAsync();
        await WaitUntilAsync(() => h.Core.Ips.Resent.Count == 1);

        h.Core.Clock.Now += h.Options.Ownership;
        await h.Core.RecoverAsync(h.Id);
        Assert.Equal(TransactionStatus.Uncertain, (await h.RunAsync())!.Status);
        h.Core.Ips.Gate.SetResult();
        await stale;

        var attempt = await h.ReadAttemptAsync();
        Assert.Null(attempt.Response);
        Assert.Contains("Abandoned", attempt.TransportFailure);
        Assert.Equal(TransactionStatus.Uncertain, (await h.Core.ReadAsync(h.Id)).Payment.CurrentStatus);
        Assert.Single(h.Core.Ips.Resent);
    }

    [Fact]
    public async Task Discovery_covers_only_message_types_without_an_investigation()
    {
        await using var h = await Harness.CreateAsync();
        h.Core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost", []));
        var pacs008 = await h.Core.AcceptAsync("investigated");
        await h.Core.ProcessAsync(pacs008);
        h.Core.Clock.Now += FirstDelay;

        var due = await h.DueAsync();

        Assert.Equal([h.Id], due);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    // Fails the commit that records the attempt's result, after its reply was saved.
    private sealed class CrashWhenResultIsSaved : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            eventData.Context!.ChangeTracker.Entries<ResendRow>().Any(entry => entry.Entity.Outcome is not null)
                ? throw new SimulatedCrash()
                : ValueTask.FromResult(result);
    }

    // A signing policy that refuses to send the frozen message.
    private sealed class RefusingProtocol : IInvestigationProtocol
    {
        public bool MaySend(SubmissionMessageKind? disposition) => false;

        public string Build(AcceptedPacs008 accepted, IpsReplyCorrelation original, InvestigationIdentity identity) =>
            throw new NotSupportedException();

        public Task<SigningResult> SignAsync(string xml, CancellationToken cancellationToken) => throw new NotSupportedException();

        public InvestigationReply Interpret(IpsSubmissionResponse response, IpsReplyCorrelation original, string investigationMessageId) =>
            throw new NotSupportedException();
    }

    private sealed class Harness(ProcessingHarness core, Guid id) : IAsyncDisposable
    {
        public ProcessingHarness Core { get; } = core;
        public Guid Id { get; } = id;
        public InvestigationOptions Options { get; set; } = new();
        public IInvestigationProtocol? Protocol { get; set; }

        // The initial send loses its reply, so the payment is uncertain with an unknown outcome.
        public static async Task<Harness> CreateAsync()
        {
            var core = await ProcessingHarness.CreateAsync();
            core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost outcome", []));
            var id = await core.AcceptPacs009Async();
            await core.ProcessAsync(id);
            core.Ips.Behavior = null;
            return new(core, id);
        }

        public async Task<PaymentOutcome?> RunAsync(IInterceptor? interceptor = null, CancellationToken token = default)
        {
            await using var s = Core.Database.Session(interceptor is null ? [] : [interceptor]);
            var policy = new Pacs008SigningPolicy(false, false);
            var protocol = Protocol ?? new InvestigationProtocol(new(policy, Core.Clock), Core.Certificates, policy, new([Core.IpsCertificate]));
            return await new OutgoingDuplicateResend(s.Payments, s.Work, new PaymentPreparationRepository(s.Context),
                new ResendRepository(s.Context), s.Unit, protocol, Core.Ips, new IpsReplyInterpreter([Core.IpsCertificate]), Options, Core.Clock)
                .ProcessAsync(Id, token);
        }

        public async Task<IReadOnlyList<Guid>> DueAsync()
        {
            await using var s = Core.Database.Session();
            return await new ResendRepository(s.Context).FindDueAsync(Core.Clock.Now, Options, default);
        }

        public async Task<ResendAttempt> ReadAttemptAsync()
        {
            await using var s = Core.Database.Session();
            return (await new ResendRepository(s.Context).ReadAsync(Id, default))!;
        }

        public async Task<int> CallbacksAsync()
        {
            await using var s = Core.Database.Session();
            return await s.Context.Set<OutgoingStatusDeliveryRow>().CountAsync(row => row.PaymentId == Id);
        }

        public ValueTask DisposeAsync() => Core.DisposeAsync();
    }
}
