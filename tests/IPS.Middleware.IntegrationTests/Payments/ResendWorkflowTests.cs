using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Investigation;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Payments;

[Collection("Investigation SQL timing")]
public sealed class ResendWorkflowTests
{
    private static readonly DateTimeOffset Deadline = Pacs008Fixture.Request().AcceptanceDateTime!.Value + TimeSpan.FromHours(24);

    [Fact]
    public async Task Not_found_resends_the_exact_original_once()
    {
        await using var h = await Harness.CreateAsync();
        Assert.Equal(TransactionStatus.Resending, (await h.InvestigateAsync())!.Status);
        Assert.Contains(h.Id, await h.DueResendsAsync());
        Assert.Empty(await h.DueInvestigationsAsync());

        var outcome = (await h.ResendAsync())!;
        Assert.Equal(TransactionStatus.Accepted, outcome.Status);
        Assert.Equal(StatusSource.Investigation, outcome.Source);
        Assert.Equal(TransactionStatus.Accepted, (await h.ResendAsync())!.Status);
        Assert.Equal(2, h.Payments.Count);
        Assert.Equal(h.Payments[0], h.Payments[1]);

        var resend = await h.ReadResendAsync();
        Assert.Equal(1, resend.Number);
        Assert.Equal(IpsReplyStatus.Accepted, resend.Result!.Status);
        Assert.Equal(MessageJournalStatus.SendStarted, resend.Request!.Status);
        Assert.Equal(MessageJournalStatus.Processed, resend.Response!.Status);

        await using var s = h.Core.Database.Session();
        var original = (await s.Submissions.ReadJournalAsync(h.Id, default))
            .Single(x => x.InvestigationId is null && x.ResendId is null && x.Direction == OutgoingMessageDirection.Outbound);
        Assert.Equal(original.Id, resend.Request.OriginatingMessageId);
        Assert.Equal(503, (await s.Submissions.ReadAsync(h.Id, default))!.Response!.HttpStatusCode);
        Assert.Contains("payment.resending-started", (await s.Payments.ReadEventsAsync(h.Id, default)).Select(e => e.Name));
        Assert.Single(await s.Context.Set<OutgoingStatusDeliveryRow>().Where(p => p.PaymentId == h.Id).ToListAsync());
    }

    [Fact]
    public async Task Rejected_resend_is_final()
    {
        await using var h = await Harness.CreateAsync();
        h.Core.Ips.Behavior = (reply, _) => h.Core.Ips.RespondAsync(reply with
        {
            GroupStatus = "RJCT",
            TransactionStatus = "RJCT",
            ReasonCode = "AC01"
        }, "RJCT/1009");
        await h.InvestigateAsync();
        Assert.Equal(TransactionStatus.Rejected, (await h.ResendAsync())!.Status);
        Assert.Equal(IpsReplyStatus.Rejected, (await h.ReadResendAsync()).Result!.Status);
        await using var s = h.Core.Database.Session();
        Assert.Contains("payment.resending-started", (await s.Payments.ReadEventsAsync(h.Id, default)).Select(e => e.Name));
        Assert.Single(await s.Context.Set<OutgoingStatusDeliveryRow>().Where(p => p.PaymentId == h.Id).ToListAsync());
    }

    [Fact]
    public async Task Cycle_limit_counts_investigations_not_resends()
    {
        await using var h = await Harness.CreateAsync();
        h.Options = new(maxCycles: 2, maxResends: 3);
        h.Core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost outcome", []));
        Assert.Equal(TransactionStatus.Resending, (await h.InvestigateAsync())!.Status);
        Assert.Equal(TransactionStatus.Uncertain, (await h.ResendAsync())!.Status);

        // The second investigation still runs; its unresolved result then reaches the two-cycle limit.
        h.InvestigationAnswer = "1017";
        h.Core.Clock.Now += TimeSpan.FromSeconds(30);
        Assert.Equal(TransactionStatus.ManualReview, (await h.InvestigateAsync())!.Status);
        Assert.Equal(2, h.Investigations);
        Assert.Equal(1, h.Resends);
    }

    [Fact]
    public async Task Lost_resend_reply_is_investigated_never_resent()
    {
        await using var h = await Harness.CreateAsync();
        h.Core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost outcome", []));
        await h.InvestigateAsync();
        Assert.Equal(TransactionStatus.Uncertain, (await h.ResendAsync())!.Status);
        Assert.Equal(h.Core.Clock.Now + TimeSpan.FromSeconds(30), (await h.Core.ReadAsync(h.Id)).NextActionAtUtc);
        Assert.Equal(MessageJournalStatus.Failed, (await h.ReadResendAsync()).Response!.Status);

        // IPS processed the resend but its reply was lost: the next investigation finds the original.
        h.InvestigationAnswer = "ACCP";
        h.Core.Clock.Now += TimeSpan.FromSeconds(30);
        Assert.Equal(TransactionStatus.Accepted, (await h.InvestigateAsync())!.Status);
        Assert.Equal(2, h.Payments.Count);
        Assert.Equal(2, h.Investigations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task Resends_are_limited_separately_from_investigation_cycles(int maxResends)
    {
        await using var h = await Harness.CreateAsync();
        h.Options = new(maxResends: maxResends);
        h.Core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost outcome", []));
        for (var resend = 0; resend < maxResends; resend++)
        {
            Assert.Equal(TransactionStatus.Resending, (await h.InvestigateAsync())!.Status);
            Assert.Equal(TransactionStatus.Uncertain, (await h.ResendAsync())!.Status);
            h.Core.Clock.Now += TimeSpan.FromMinutes(15);
        }

        var exhausted = (await h.InvestigateAsync())!;
        Assert.Equal(TransactionStatus.ManualReview, exhausted.Status);
        Assert.Contains($"already resent {maxResends} time(s)", exhausted.Details.Description);
        Assert.Equal(1 + maxResends, h.Payments.Count);
        Assert.Equal(1 + maxResends, h.Investigations);
        await using var s = h.Core.Database.Session();
        Assert.Single(await s.Context.Set<OutgoingStatusDeliveryRow>().Where(p => p.PaymentId == h.Id).ToListAsync());
    }

    [Fact]
    public async Task One_not_found_result_authorizes_only_one_resend()
    {
        await using var h = await Harness.CreateAsync();
        await h.InvestigateAsync();
        var resend = await h.ReadResendAsync();
        await using var s = h.Core.Database.Session();
        var payment = (await s.Payments.FindAsync(h.Id, default))!;
        var claim = s.Work.StageClaim(payment, h.Core.Clock.Now, h.Options.Ownership)!;
        await s.Unit.SaveAsync();
        new ResendRepository(s.Context).StageAuthorization(payment, claim, resend.InvestigationId, 2, h.Core.Clock.Now);
        await Assert.ThrowsAsync<UniqueConstraintException>(() => s.Unit.SaveAsync());
    }

    [Fact]
    public async Task Competing_owners_send_the_resend_once_and_stale_owner_cannot_store_a_response()
    {
        await using var h = await Harness.CreateAsync();
        await h.InvestigateAsync();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.BeforeResend = async _ => { arrived.TrySetResult(); await release.Task; };
        var first = h.ResendAsync();
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.BeforeResend = null;
        Assert.Equal(TransactionStatus.Resending, (await h.ResendAsync())!.Status);
        Assert.Equal(1, h.Resends);

        h.Core.Clock.Now += h.Options.Ownership;
        await h.Core.RecoverAsync(h.Id);
        Assert.Equal(TransactionStatus.Uncertain, (await h.ResendAsync())!.Status);
        release.SetResult();
        await first;

        Assert.Equal(1, h.Resends);
        var resend = await h.ReadResendAsync();
        Assert.Null(resend.Response);
        Assert.Contains("Abandoned", resend.TransportFailure);
    }

    [Fact]
    public async Task Abandoned_resend_marker_is_investigated_never_repeated()
    {
        await using var h = await Harness.CreateAsync();
        await h.InvestigateAsync();
        using var cancelled = new CancellationTokenSource();
        h.BeforeResend = _ => { cancelled.Cancel(); throw new OperationCanceledException(cancelled.Token); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.ResendAsync(token: cancelled.Token));
        h.BeforeResend = null;
        h.Core.Clock.Now += h.Options.Ownership;
        await h.Core.RecoverAsync(h.Id);
        Assert.Contains(h.Id, await h.DueResendsAsync());

        var abandoned = (await h.ResendAsync())!;
        Assert.Equal(TransactionStatus.Uncertain, abandoned.Status);
        Assert.Equal(StatusSource.Recovery, abandoned.Source);
        Assert.Contains(h.Id, await h.DueInvestigationsAsync());
        h.InvestigationAnswer = "ACCP";
        Assert.Equal(TransactionStatus.Accepted, (await h.InvestigateAsync())!.Status);
        Assert.Equal(1, h.Resends);
    }

    [Theory]
    [InlineData("authorization")]
    [InlineData("ready")]
    [InlineData("marker")]
    [InlineData("response")]
    [InlineData("outcome")]
    public async Task Crash_at_each_resend_checkpoint_resumes_without_a_second_send(string checkpoint)
    {
        await using var h = await Harness.CreateAsync();
        var crash = new CrashWhenSaving(db => checkpoint switch
        {
            "authorization" => db.ChangeTracker.Entries<ResendRow>().Any(e => e.State == EntityState.Added),
            "ready" => ResendMessages(db).Any(e => e.State == EntityState.Added && e.Entity.Direction == OutgoingMessageDirection.Outbound),
            "marker" => ResendMessages(db).Any(e => e.Entity.Status == MessageJournalStatus.SendStarted && e.State == EntityState.Modified),
            "response" => ResendMessages(db).Any(e => e.State == EntityState.Added && e.Entity.Direction == OutgoingMessageDirection.Response),
            _ => db.ChangeTracker.Entries<ResendRow>().Any(e => e.Entity.Outcome is not null)
        });
        if (checkpoint == "authorization")
        {
            await Assert.ThrowsAsync<SimulatedCrash>(() => h.InvestigateAsync(crash));
        }
        else
        {
            await h.InvestigateAsync();
            await Assert.ThrowsAsync<SimulatedCrash>(() => h.ResendAsync(crash));
        }

        // Whatever IPS saw before the crash, a further investigation now finds the payment.
        h.InvestigationAnswer = "ACCP";
        h.Core.Clock.Now += h.Options.Ownership;
        await h.Core.RecoverAsync(h.Id);
        Assert.Equal(TransactionStatus.Accepted, (await h.SettleAsync()).Status);
        Assert.Equal(1, h.Resends);
        Assert.Equal(1, (await h.ReadResendAsync()).Number);
        // Only a resend that reached IPS without a stored response needs another investigation.
        Assert.Equal(checkpoint == "response" ? 2 : 1, h.Investigations);
    }

    [Fact]
    public async Task Exact_deadline_prevents_resend_and_requires_manual_review()
    {
        await using var h = await Harness.CreateAsync();
        await h.InvestigateAsync();
        h.Core.Clock.Now = Deadline;
        Assert.Equal(TransactionStatus.ManualReview, (await h.ResendAsync())!.Status);
        Assert.Equal(0, h.Resends);
        var resend = await h.ReadResendAsync();
        Assert.Equal(IpsReplyStatus.Unresolved, resend.Result!.Status);
        Assert.Contains("window", resend.Result.Details.Description);
        Assert.Null(resend.TransportFailure);
        await using var s = h.Core.Database.Session();
        Assert.Single(await s.Context.Set<OutgoingStatusDeliveryRow>().Where(p => p.PaymentId == h.Id).ToListAsync());
    }

    [Fact]
    public async Task Saved_resend_response_replays_after_deadline_without_another_call()
    {
        await using var h = await Harness.CreateAsync();
        await h.InvestigateAsync();
        var crash = new CrashWhenSaving(db => db.ChangeTracker.Entries<ResendRow>().Any(e => e.Entity.Outcome is not null));
        await Assert.ThrowsAsync<SimulatedCrash>(() => h.ResendAsync(crash));
        h.Core.Clock.Now = Deadline + TimeSpan.FromHours(1);
        await h.Core.RecoverAsync(h.Id);
        Assert.Equal(TransactionStatus.Accepted, (await h.ResendAsync())!.Status);
        Assert.Equal(1, h.Resends);
    }

    [Fact]
    public async Task Resend_call_is_clamped_to_remaining_window_and_failure_is_durable()
    {
        await using var h = await Harness.CreateAsync();
        await h.InvestigateAsync();
        h.Core.Clock.Now = Deadline - TimeSpan.FromMilliseconds(100);
        h.BeforeResend = token => Task.Delay(Timeout.Infinite, token);
        Assert.Equal(TransactionStatus.Uncertain, (await h.ResendAsync().WaitAsync(TimeSpan.FromSeconds(10)))!.Status);
        Assert.Equal(Deadline, (await h.Core.ReadAsync(h.Id)).NextActionAtUtc);
        var resend = await h.ReadResendAsync();
        Assert.Null(resend.Response);
        Assert.NotNull(resend.TransportFailure);
    }

    [Fact]
    public async Task Development_unsigned_original_is_resent_only_while_policy_allows_it()
    {
        await using var h = await Harness.CreateAsync(developmentUnsigned: true);
        Assert.Equal(TransactionStatus.Resending, (await h.InvestigateAsync())!.Status);
        Assert.Equal(TransactionStatus.Resending, (await h.ResendAsync())!.Status);
        Assert.Equal(0, h.Resends);
        Assert.Equal(h.Core.Clock.Now + h.Options.PreparationRetryDelay, (await h.Core.ReadAsync(h.Id)).NextActionAtUtc);

        h.Policy = new(true, true);
        h.Core.Clock.Now += h.Options.PreparationRetryDelay;
        Assert.Equal(TransactionStatus.Accepted, (await h.ResendAsync())!.Status);
        Assert.Equal(h.Payments[0], h.Payments[1]);
        Assert.Equal(SubmissionMessageKind.DevelopmentUnsigned, (await h.ReadResendAsync()).Request!.Disposition);
    }

    private static IEnumerable<EntityEntry<OutgoingMessageRow>> ResendMessages(DbContext db) =>
        db.ChangeTracker.Entries<OutgoingMessageRow>().Where(e => e.Entity.ResendId is not null);

    // Throws before the selected change commits, so its transaction rolls back.
    private sealed class CrashWhenSaving(Func<DbContext, bool> when) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            when(eventData.Context!) ? throw new SimulatedCrash() : ValueTask.FromResult(result);
    }

    // Answers pacs.028 itself and forwards pacs.008 resends to the shared IPS simulator.
    private sealed class Harness(ProcessingHarness core, Guid id) : IIpsTransport, IAsyncDisposable
    {
        public ProcessingHarness Core { get; } = core;
        public Guid Id { get; } = id;
        public InvestigationOptions Options { get; set; } = new();
        public Pacs008SigningPolicy Policy { get; set; } = new(false, false);
        public string InvestigationAnswer { get; set; } = "1016";
        public Func<CancellationToken, Task>? BeforeResend { get; set; }
        public int Investigations { get; private set; }
        public int Resends { get; private set; }

        // Every pacs.008 IPS received, the lost initial submission included.
        public IReadOnlyList<string> Payments => Core.Ips.Received;

        // The initial reply is lost, so the payment is uncertain and its first investigation is due.
        public static async Task<Harness> CreateAsync(bool developmentUnsigned = false)
        {
            var core = await ProcessingHarness.CreateAsync(developmentUnsigned);
            core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost outcome", []));
            if (developmentUnsigned)
            {
                core.Certificates.Current = null;
            }

            var id = await core.AcceptAsync();
            await core.ProcessAsync(id);
            core.Certificates.Current = core.SigningCertificate;
            core.Ips.Behavior = null;
            core.Clock.Now += TimeSpan.FromSeconds(9);
            return new(core, id);
        }

        public async Task<PaymentOutcome?> InvestigateAsync(IInterceptor? interceptor = null)
        {
            await using var s = Core.Database.Session(interceptor is null ? [] : [interceptor]);
            return await new OutgoingInvestigation(s.Payments, s.Work, new PaymentPreparationRepository(s.Context), new InvestigationRepository(s.Context),
                new ResendRepository(s.Context), s.Unit, Protocol(), this, Options, Core.Clock).ProcessAsync(Id, default);
        }

        public async Task<PaymentOutcome?> ResendAsync(IInterceptor? interceptor = null, CancellationToken token = default)
        {
            await using var s = Core.Database.Session(interceptor is null ? [] : [interceptor]);
            return await new OutgoingResend(s.Payments, s.Work, new PaymentPreparationRepository(s.Context), new InvestigationRepository(s.Context),
                new ResendRepository(s.Context), s.Unit, Protocol(), this, new IpsReplyInterpreter([Core.IpsCertificate]), Options, Core.Clock)
                .ProcessAsync(Id, token);
        }

        // Runs whichever workflow owns the current state until the payment leaves investigation.
        public async Task<PaymentOutcome> SettleAsync()
        {
            for (var step = 0; step < 5; step++)
            {
                var status = (await Core.ReadAsync(Id)).Payment.CurrentStatus;
                var outcome = status == TransactionStatus.Resending ? await ResendAsync() : await InvestigateAsync();
                if (outcome!.Status is not (TransactionStatus.Resending or TransactionStatus.Uncertain or TransactionStatus.Investigating))
                {
                    return outcome;
                }
            }

            throw new InvalidOperationException("The payment did not settle.");
        }

        public async Task<ResendAttempt> ReadResendAsync()
        {
            await using var s = Core.Database.Session();
            return (await new ResendRepository(s.Context).ReadAsync(Id, default))!;
        }

        public Task<IReadOnlyList<Guid>> DueResendsAsync() => Core.FindDueAsync(TransactionStatus.Resending, Core.Clock.Now);

        public async Task<IReadOnlyList<Guid>> DueInvestigationsAsync()
        {
            await using var s = Core.Database.Session();
            return await new InvestigationRepository(s.Context).FindDueAsync(Core.Clock.Now, Options, default);
        }

        public async Task<IpsSubmissionResponse> SendAsync(string xml, CancellationToken cancellationToken)
        {
            if (IpsReplies.IsInvestigation(xml))
            {
                Investigations++;
                return await IpsReplies.AnswerInvestigationAsync(Core.IpsCertificate, xml, InvestigationAnswer);
            }

            Resends++;
            if (BeforeResend is { } beforeResend)
            {
                await beforeResend(cancellationToken);
            }

            return await Core.Ips.SendAsync(xml, cancellationToken);
        }

        public ValueTask DisposeAsync() => Core.DisposeAsync();

        private InvestigationProtocol Protocol() =>
            new(new(Policy, Core.Clock), Core.Certificates, Policy, new([Core.IpsCertificate]));
    }
}
