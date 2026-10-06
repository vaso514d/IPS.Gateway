using System.Xml.Linq;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Investigation;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Payments;

[Collection("Investigation SQL timing")]
public sealed class InvestigationWorkflowTests
{
    [Theory]
    [InlineData("ACCP", TransactionStatus.Accepted)]
    [InlineData("RJCT", TransactionStatus.Rejected)]
    [InlineData("1016", TransactionStatus.Resending)]
    [InlineData("1017", TransactionStatus.Uncertain)]
    [InlineData("malformed", TransactionStatus.Uncertain)]
    public async Task Investigation_results_commit_without_the_investigation_sending_the_payment(string answer, TransactionStatus expected)
    {
        await using var h = await Harness.Create();
        h.Answer = answer;
        Assert.Empty(await h.Due());
        Assert.Equal(TransactionStatus.Uncertain, (await h.Run())!.Status);
        Assert.Equal(0, h.Calls);
        h.Core.Clock.Now += TimeSpan.FromSeconds(9);
        Assert.Contains(h.Id, await h.Due());
        Assert.Equal(expected, (await h.Run())!.Status);
        Assert.Equal(1, h.Calls);
        Assert.Single(h.Core.Ips.Received);
        await using var s = h.Core.Database.Session();
        var attempt = (await new InvestigationRepository(s.Context).ReadAsync(h.Id, default))!;
        Assert.NotNull(attempt.Result);
        Assert.Equal(MessageJournalStatus.SendStarted, attempt.Request!.Status);
        Assert.Equal(answer is "1017" or "malformed" ? MessageJournalStatus.Failed : MessageJournalStatus.Processed, attempt.Response!.Status);
        Assert.Equal(4, (await s.Submissions.ReadJournalAsync(h.Id, default)).Count);
        Assert.NotNull((await s.Submissions.ReadAsync(h.Id, default))!.Response);
        if (expected is TransactionStatus.Accepted or TransactionStatus.Rejected)
        {
            Assert.Single(await s.Context.Set<OutgoingStatusDeliveryRow>().Where(p => p.PaymentId == h.Id).ToListAsync());
        }

        if (answer == "1016")
        {
            Assert.Empty(await h.Due());
            await h.Run();
            Assert.Equal(1, h.Calls);
            Assert.Equal(InvestigationOutcome.NotFound, attempt.Result!.Outcome);
        }
    }

    [Fact]
    public async Task Retry_boundaries_and_separate_cycle_limit_survive_new_scopes()
    {
        await using var h = await Harness.Create();
        h.Answer = "1017";
        h.Options = new(maxCycles: 2);
        h.Core.Clock.Now += TimeSpan.FromSeconds(9);
        await h.Run();
        h.Core.Clock.Now += TimeSpan.FromSeconds(30) - TimeSpan.FromTicks(1);
        Assert.Empty(await h.Due());
        await h.Run();
        Assert.Equal(1, h.Calls);
        h.Core.Clock.Now += TimeSpan.FromTicks(1);
        Assert.Equal(TransactionStatus.ManualReview, (await h.Run())!.Status);
        Assert.Equal(2, h.Calls);
        Assert.Empty(await h.Due());
    }

    [Fact]
    public async Task Changing_configuration_cannot_extend_a_committed_investigation_deadline()
    {
        await using var h = await Harness.Create();
        h.Options = new(window: TimeSpan.FromMinutes(2));
        h.Answer = "1017";
        h.Core.Clock.Now += TimeSpan.FromSeconds(9);
        await h.Run();
        h.Options = new(window: TimeSpan.FromHours(24));
        h.Core.Clock.Now += TimeSpan.FromMinutes(3);
        Assert.Equal(TransactionStatus.ManualReview, (await h.Run())!.Status);
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task Exact_window_deadline_prevents_new_remote_call()
    {
        await using var h = await Harness.Create();
        h.Core.Clock.Now = Pacs008Fixture.Request().AcceptanceDateTime!.Value + TimeSpan.FromHours(24);
        Assert.Equal(TransactionStatus.ManualReview, (await h.Run())!.Status);
        Assert.Equal(0, h.Calls);
    }

    [Fact]
    public async Task Saved_response_replays_after_window_without_another_call()
    {
        await using var h = await Harness.Create();
        h.Core.Clock.Now += TimeSpan.FromSeconds(9);
        await Assert.ThrowsAsync<SimulatedCrash>(() => h.Run(new CrashOnSave(e => e.Context.ChangeTracker.Entries<InvestigationRow>()
            .Any(p => p.Entity.Outcome is not null))));
        Assert.Equal(1, h.Calls);
        h.Core.Clock.Now = Pacs008Fixture.Request().AcceptanceDateTime!.Value + TimeSpan.FromHours(25);
        await h.Core.RecoverAsync(h.Id);
        Assert.Equal(TransactionStatus.Accepted, (await h.Run())!.Status);
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task Abandoned_investigation_marker_never_repeats_the_marked_send()
    {
        await using var h = await Harness.Create();
        h.Core.Clock.Now += TimeSpan.FromSeconds(9);
        h.BeforeResponse = () => throw new OperationCanceledException();
        // External cancellation leaves the committed marker and no fabricated HTTP evidence.
        using var cancelled = new CancellationTokenSource();
        h.BeforeResponse = () => { cancelled.Cancel(); throw new OperationCanceledException(cancelled.Token); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Run(token: cancelled.Token));
        h.Core.Clock.Now += h.Options.Ownership;
        await h.Core.RecoverAsync(h.Id);
        h.BeforeResponse = null;
        Assert.Equal(TransactionStatus.Uncertain, (await h.Run())!.Status);
        Assert.Equal(1, h.Calls);
        await using var s = h.Core.Database.Session();
        var attempt = (await new InvestigationRepository(s.Context).ReadAsync(h.Id, default))!;
        Assert.Null(attempt.Response);
        Assert.Contains("Abandoned", attempt.TransportFailure);
        h.Core.Clock.Now += TimeSpan.FromSeconds(30);
        Assert.Equal(TransactionStatus.Accepted, (await h.Run())!.Status);
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task Deferred_signing_reuses_identifiers_and_unsigned_xml_without_consuming_another_cycle()
    {
        await using var h = await Harness.Create();
        h.Core.Clock.Now += TimeSpan.FromSeconds(9);
        h.Core.Certificates.Unavailable = true;
        await h.Run();
        Assert.Equal(0, h.Calls);
        InvestigationAttempt before;
        await using (var s = h.Core.Database.Session())
        {
            before = (await new InvestigationRepository(s.Context).ReadAsync(h.Id, default))!;
        }

        h.Core.Certificates.Unavailable = false;
        h.Core.Clock.Now += TimeSpan.FromSeconds(1);
        await h.Run();
        await using var read = h.Core.Database.Session();
        var after = (await new InvestigationRepository(read.Context).ReadAsync(h.Id, default))!;
        Assert.Equivalent(before.Identity, after.Identity, strict: true);
        Assert.Equal(before.UnsignedXml, after.UnsignedXml);
    }

    [Fact]
    public async Task Competing_scopes_submit_once_and_stale_owner_cannot_append_evidence()
    {
        await using var h = await Harness.Create();
        h.Core.Clock.Now += TimeSpan.FromSeconds(9);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.WaitBeforeResponse = async () => { arrived.SetResult(); await release.Task; };
        var first = h.Run();
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await h.Run();
        Assert.Equal(1, h.Calls);
        h.Core.Clock.Now += h.Options.Ownership;
        await h.Core.RecoverAsync(h.Id);
        await h.Run();
        release.SetResult();
        await first;
        await using var read = h.Core.Database.Session();
        var attempt = (await new InvestigationRepository(read.Context).ReadAsync(h.Id, default))!;
        Assert.Null(attempt.Response);
        Assert.Equal(InvestigationOutcome.Unresolved, attempt.Result!.Outcome);
    }

    [Fact]
    public async Task Final_interpretation_failure_rolls_back_outcome_events_and_callback_then_replays()
    {
        await using var h = await Harness.Create();
        h.Core.Clock.Now += TimeSpan.FromSeconds(9);
        await Assert.ThrowsAsync<SimulatedCrash>(() => h.Run(new FailOutcomeWrite()));
        await using (var read = h.Core.Database.Session())
        {
            Assert.Equal(TransactionStatus.Investigating, (await read.Payments.FindAsync(h.Id, default))!.CurrentStatus);
            Assert.Empty(await read.Context.Set<OutgoingStatusDeliveryRow>().Where(p => p.PaymentId == h.Id).ToListAsync());
            Assert.Null((await new InvestigationRepository(read.Context).ReadAsync(h.Id, default))!.Result);
        }
        h.Core.Clock.Now += h.Options.Ownership;
        await h.Core.RecoverAsync(h.Id);
        Assert.Equal(TransactionStatus.Accepted, (await h.Run())!.Status);
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task Recovery_after_initial_submission_abandonment_has_no_additional_first_delay()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptAsync();
        using var stop = new CancellationTokenSource();
        core.Ips.Behavior = (_, _) => { stop.Cancel(); throw new OperationCanceledException(stop.Token); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => core.ProcessAsync(id, stop.Token));
        core.Clock.Now += ProcessingHarness.Ownership;
        await core.RecoverAsync(id);
        // Harness shares the fixture here; dispose it only through core.
        var h = new Harness(core, id);
        Assert.Contains(id, await h.Due());
        Assert.Equal(TransactionStatus.Accepted, (await h.Run())!.Status);
    }

    [Fact]
    public async Task Registration_composes_investigation_and_resend_workflows()
    {
        await using var h = await Harness.Create();
        h.Core.Clock.Now += TimeSpan.FromSeconds(9);
        using var db = h.Core.Database.Context();
        var services = new ServiceCollection().AddPersistence(db.Database.GetConnectionString()!).AddOutgoingInvestigation();
        services.AddSingleton(h.Options);
        services.AddSingleton<TimeProvider>(h.Core.Clock);
        services.AddSingleton<IIpsTransport>(h);
        services.AddSingleton<IIpsReplyInterpreter>(new IpsReplyInterpreter([h.Core.IpsCertificate]));
        var policy = new Pacs008SigningPolicy(false, false);
        services.AddSingleton<IInvestigationProtocol>(new InvestigationProtocol(new(policy, h.Core.Clock), h.Core.Certificates, policy, new([h.Core.IpsCertificate])));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<OutgoingResend>());
        Assert.Equal(TransactionStatus.Accepted, (await scope.ServiceProvider.GetRequiredService<OutgoingInvestigation>().ProcessAsync(h.Id, default))!.Status);
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task Call_timeout_is_clamped_to_remaining_window_and_failure_is_durable()
    {
        await using var h = await Harness.Create();
        var deadline = Pacs008Fixture.Request().AcceptanceDateTime!.Value + TimeSpan.FromHours(24);
        h.Core.Clock.Now = deadline - TimeSpan.FromMilliseconds(100);
        h.AwaitRemote = ct => Task.Delay(Timeout.Infinite, ct);
        await h.Run().WaitAsync(TimeSpan.FromSeconds(10));
        await using var read = h.Core.Database.Session();
        var attempt = (await new InvestigationRepository(read.Context).ReadAsync(h.Id, default))!;
        Assert.Null(attempt.Response);
        Assert.NotNull(attempt.TransportFailure);
        Assert.Equal(InvestigationOutcome.Unresolved, attempt.Result!.Outcome);
        Assert.Equal(deadline, (await h.Core.ReadAsync(h.Id)).NextActionAtUtc);
        h.Core.Clock.Now = deadline;
        Assert.Equal(TransactionStatus.ManualReview, (await h.Run())!.Status);
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task Discovery_orders_effective_due_times_across_normal_and_recovered_work()
    {
        await using var h = await Harness.Create();
        var recovered = await h.Core.AcceptAsync("recovered");
        await h.Core.ProcessAsync(recovered);
        await using (var s = h.Core.Database.Session())
        {
            var payment = (await s.Payments.FindAsync(recovered, default))!;
            Assert.NotNull(s.Work.StageClaim(payment, h.Core.Clock.Now, TimeSpan.FromSeconds(5)));
            payment.BeginInvestigation(h.Core.Clock.Now);
            await s.Unit.SaveAsync();
        }
        h.Core.Clock.Now += TimeSpan.FromSeconds(5);
        await h.Core.RecoverAsync(recovered);
        h.Core.Clock.Now += TimeSpan.FromSeconds(5);
        Assert.Equal(new[] { recovered, h.Id }, await h.Due());
    }

    private sealed class FailOutcomeWrite : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<InvestigationRow>().Any(p => p.Entity.Outcome is not null))
            {
                throw new SimulatedCrash();
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class Harness(ProcessingHarness core, Guid id) : IIpsTransport, IAsyncDisposable
    {
        public ProcessingHarness Core { get; } = core;
        public Guid Id { get; } = id;
        public InvestigationOptions Options { get; set; } = new();
        public string Answer { get; set; } = "ACCP";
        public int Calls { get; private set; }
        public Func<CancellationToken, Task>? AwaitRemote { get; set; }
        public Action? BeforeResponse { get; set; }
        public Func<Task>? WaitBeforeResponse { get; set; }
        public static async Task<Harness> Create()
        {
            var core = await ProcessingHarness.CreateAsync();
            core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost outcome", []));
            var id = await core.AcceptAsync();
            await core.ProcessAsync(id);
            return new(core, id);
        }
        public async Task<PaymentOutcome?> Run(IInterceptor? interceptor = null, CancellationToken token = default)
        {
            await using var s = Core.Database.Session(interceptor is null ? [] : [interceptor]);
            var policy = new Pacs008SigningPolicy(false, false);
            var protocol = new InvestigationProtocol(new(policy, Core.Clock), Core.Certificates, policy, new([Core.IpsCertificate]));
            return await new OutgoingInvestigation(s.Payments, s.Work, new PaymentPreparationRepository(s.Context), new InvestigationRepository(s.Context),
                new ResendRepository(s.Context), s.Unit, protocol, this, Options, Core.Clock).ProcessAsync(Id, token);
        }
        public async Task<IReadOnlyList<Guid>> Due()
        {
            await using var s = Core.Database.Session();
            return await new InvestigationRepository(s.Context).FindDueAsync(Core.Clock.Now, Options, default);
        }
        public async Task<IpsSubmissionResponse> SendAsync(string xml, CancellationToken cancellationToken)
        {
            Calls++;
            if (AwaitRemote is not null)
            {
                await AwaitRemote(cancellationToken);
            }

            BeforeResponse?.Invoke();
            if (WaitBeforeResponse is not null)
            {
                await WaitBeforeResponse();
            }

            if (Answer == "malformed")
            {
                return new(503, "<bad", [new("X-Evidence", "preserved")]);
            }

            return await IpsReplies.AnswerInvestigationAsync(Core.IpsCertificate, xml, Answer);
        }
        public Task<IpsSubmissionResponse> ResendAsync(string xml, CancellationToken cancellationToken) => SendAsync(xml, cancellationToken);
        public ValueTask DisposeAsync() => Core.DisposeAsync();
    }
}

// These correctness tests use the real two-second evidence budget, not a benchmark under parallel fixture startup.
[CollectionDefinition("Investigation SQL timing", DisableParallelization = true)]
public sealed class InvestigationSqlTimingCollection;
