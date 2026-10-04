using System.Data.Common;
using IPS.Middleware.Application.Abstractions.Inbound;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Repositories.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using IPS.Middleware.Infrastructure.UnitOfWork;
using IPS.Middleware.IntegrationTests.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

public sealed class IncomingCbsProcessingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Final_core_reply_is_stored_before_acceptance_and_followup_is_not_required()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterAsync();
        var result = await test.ProcessAsync(id);
        Assert.True(result!.IpsDecision!.Accepted);
        Assert.Equal(CoreOutcome.Accepted, result.CoreStatus);
        Assert.Equal(IncomingFollowUp.None, result.FollowUp);
        Assert.Equal(1, test.Cbs.Submissions);
        await using var db = test.Database.Context();
        var stored = (await new IncomingProcessingRepository(db).ReadAsync(id, default))!;
        Assert.Equal("{\"Status\":\"ACCP\",\"CoreReference\":\"credit-1\"}", Assert.Single(stored.Calls).Completion!.Response!.Body);
        Assert.True(stored.Calls[0].Consumed);
        Assert.Null(stored.FollowUpAtUtc);
        Assert.Empty(await new IncomingPaymentWorkRepository(db).FindDueAsync(Now.AddDays(1), 100, default));
        Assert.Equal(result, await test.ProcessAsync(id));
        Assert.Equal(1, test.Cbs.Submissions);
    }

    [Theory]
    [InlineData("{\"Status\":\"RJCT\",\"ReasonCode\":\"AC04\"}", false, "AC04", IncomingFollowUp.None)]
    [InlineData("{}", false, "MS03", IncomingFollowUp.ReconciliationRequired)]
    [InlineData("{\"Status\":\"PDNG\"}", false, "MS03", IncomingFollowUp.ReconciliationRequired)]
    public async Task Final_rejection_or_unresolved_result_keeps_the_correct_followup(string body, bool accepted, string reason, IncomingFollowUp followUp)
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterAsync();
        test.Cbs.Submit = _ => Task.FromResult(new CoreResponse(200, body));
        test.Cbs.Query = _ => Task.FromResult(new CoreResponse(404, ""));
        var result = (await test.ProcessAsync(id))!;
        Assert.Equal(accepted, result.IpsDecision!.Accepted);
        Assert.Equal(reason, result.IpsDecision.ReasonCode);
        Assert.Equal(followUp, result.FollowUp);
        await using var db = test.Database.Context();
        var stored = (await new IncomingProcessingRepository(db).ReadAsync(id, default))!;
        Assert.Equal(followUp == IncomingFollowUp.None ? null : Now.AddSeconds(10), stored.FollowUpAtUtc);
        Assert.All(stored.Calls, call => Assert.True(call.Consumed));
    }

    [Fact]
    public async Task A_credit_before_a_lost_reply_is_queried_without_another_submission()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterAsync();
        var credited = false;
        test.Cbs.Submit = _ => { credited = true; throw new IOException("Reply lost after credit."); };
        test.Cbs.Query = _ => Task.FromResult(new CoreResponse(200, credited ? "{\"Status\":\"ACCP\"}" : "{\"Status\":\"PDNG\"}"));
        Assert.True((await test.ProcessAsync(id))!.IpsDecision!.Accepted);
        Assert.Equal((1, 1), (test.Cbs.Submissions, test.Cbs.Queries));
        await using var db = test.Database.Context();
        var calls = (await new IncomingProcessingRepository(db).ReadAsync(id, default))!.Calls;
        Assert.Contains("Reply lost", calls[0].Completion!.Failure);
        Assert.True(calls.All(c => c.Consumed));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(20)]
    [InlineData(45)]
    public async Task Exhausted_initial_budget_never_sends_and_has_no_uncertain_core_outcome(int elapsed)
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterAsync();
        test.Time.Now = Now.AddSeconds(elapsed);
        var result = (await test.ProcessAsync(id))!;
        Assert.Equal(CoreOutcome.NotSubmitted, result.CoreStatus);
        Assert.False(result.IpsDecision!.Accepted);
        Assert.Equal(IncomingFollowUp.None, result.FollowUp);
        Assert.Equal((0, 0), (test.Cbs.Submissions, test.Cbs.Queries));
    }

    [Theory]
    [InlineData(17, true)]
    [InlineData(18, false)]
    [InlineData(21, false)]
    public async Task Reply_cutoff_never_turns_a_late_credit_into_acceptance(int elapsed, bool accepted)
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterAsync();
        test.Cbs.Submit = _ =>
        {
            test.Time.Now = Now.AddSeconds(elapsed);
            return Task.FromResult(new CoreResponse(200, "{\"Status\":\"ACCP\"}"));
        };
        var result = (await test.ProcessAsync(id))!;
        Assert.Equal(CoreOutcome.Accepted, result.CoreStatus);
        Assert.Equal(accepted, result.IpsDecision!.Accepted);
        Assert.Equal(accepted ? IncomingFollowUp.None : IncomingFollowUp.ReversalRequired, result.FollowUp);
    }

    [Theory]
    [InlineData(1, false, false)] // Before ownership commit.
    [InlineData(2, false, false)] // After ownership commit, before marker.
    [InlineData(3, false, false)] // Marker rolled back.
    [InlineData(4, true, false)]  // Marker committed; no remote call.
    [InlineData(5, true, false)]  // CBS credited; raw response rolled back.
    [InlineData(6, true, true)]   // Raw response committed; not interpreted.
    [InlineData(7, true, true)]   // Outcome/decision transaction rolled back together.
    [InlineData(8, true, true)]   // Decision committed; restart returns it unchanged.
    public async Task Restart_resumes_only_from_committed_evidence(int phase, bool marked, bool responseStored)
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterAsync();
        await Assert.ThrowsAsync<Crash>(() => test.ProcessAsync(id, default, phase % 2 == 0 ? new CrashAfterCommit(phase / 2) : new CrashAtSave(phase)));
        await using (var db = test.Database.Context())
        {
            var stored = (await new IncomingProcessingRepository(db).ReadAsync(id, default))!;
            Assert.Equal(marked, stored.Calls.Count != 0);
            Assert.Equal(responseStored, stored.Calls.FirstOrDefault()?.Completion is not null);
            Assert.Equal(phase == 8, stored.Payment.IpsDecision is not null);
        }
        var before = test.Cbs.Submissions;
        test.Time.Now = Now.AddSeconds(46);
        var recovered = (await test.ProcessAsync(id))!;
        Assert.Equal(before, test.Cbs.Submissions);
        Assert.Equal(phase == 8, recovered.IpsDecision!.Accepted);
        Assert.Equal(phase == 8 || !marked ? IncomingFollowUp.None : responseStored
            ? IncomingFollowUp.ReversalRequired : IncomingFollowUp.ReconciliationRequired, recovered.FollowUp);
    }

    [Fact]
    public async Task Complete_reply_is_saved_when_service_stops_before_interpretation()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterAsync();
        using var shutdown = new CancellationTokenSource();
        test.Cbs.Submit = _ =>
        {
            shutdown.Cancel();
            return Task.FromResult(new CoreResponse(200, "{\"Status\":\"ACCP\"}"));
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.ProcessAsync(id, shutdown.Token));
        await using (var db = test.Database.Context())
        {
            var stored = (await new IncomingProcessingRepository(db).ReadAsync(id, default))!;
            Assert.NotNull(Assert.Single(stored.Calls).Completion);
            Assert.Null(stored.Payment.IpsDecision);
        }
        test.Time.Now = Now.AddSeconds(46);
        Assert.Equal(IncomingFollowUp.ReversalRequired, (await test.ProcessAsync(id))!.FollowUp);
        Assert.Equal(1, test.Cbs.Submissions);
    }

    [Fact]
    public async Task Competing_execution_cannot_call_core_while_an_owner_is_waiting()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<CoreResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Cbs.Submit = token => { entered.SetResult(); return answer.Task.WaitAsync(token); };
        var first = test.ProcessAsync(id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null((await test.ProcessAsync(id))!.IpsDecision);
        answer.SetResult(new(200, "{\"Status\":\"ACCP\"}"));
        Assert.True((await first)!.IpsDecision!.Accepted);
        Assert.Equal(1, test.Cbs.Submissions);
    }

    [Fact]
    public async Task Expired_owner_cannot_save_a_late_response_after_recovery_decides_rejection()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterAsync();
        test.Cbs.Submit = async _ =>
        {
            test.Time.Now = Now.AddSeconds(46);
            var recovered = (await test.ProcessAsync(id))!;
            Assert.False(recovered.IpsDecision!.Accepted);
            return new(200, "{\"Status\":\"ACCP\"}");
        };
        await test.ProcessAsync(id);
        await using var db = test.Database.Context();
        var stored = (await new IncomingProcessingRepository(db).ReadAsync(id, default))!;
        Assert.Null(Assert.Single(stored.Calls).Completion);
        Assert.False(stored.Payment.IpsDecision!.Accepted);
        Assert.Equal(IncomingFollowUp.ReconciliationRequired, stored.Payment.FollowUp);
    }

    [Fact]
    public async Task Receipt_fallback_deadline_and_original_context_do_not_move_on_duplicate_intake()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterAsync();
        test.Time.Now = Now.AddSeconds(10);
        Assert.Equal(id, await test.RegisterAsync(2));
        await using var db = test.Database.Context();
        var stored = (await new IncomingProcessingRepository(db).ReadAsync(id, default))!;
        Assert.Equal(Now, stored.Context.ReceivedAtUtc);
        Assert.Equal(Now.AddSeconds(20), stored.Context.DeadlineUtc);
    }

    private sealed class Crash : Exception;
    private sealed class CrashAtSave(int phase) : SaveChangesInterceptor
    {
        private int _calls;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (++_calls == phase) throw new Crash();
            return ValueTask.FromResult(result);
        }
    }
    private sealed class CrashAfterCommit(int commit) : DbTransactionInterceptor
    {
        private int _commits;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (++_commits == commit) throw new Crash();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Call_timeouts_are_unknown_while_shutdown_leaves_recoverable_evidence()
    {
        await using var test = await Harness.CreateAsync();
        test.Options = new(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(4));
        var id = await test.RegisterAsync();
        test.Cbs.Submit = async token => { await Task.Delay(Timeout.Infinite, token); return new(200, "{}"); };
        test.Cbs.Query = test.Cbs.Submit;
        var result = (await test.ProcessAsync(id))!;
        Assert.Equal(CoreOutcome.Unknown, result.CoreStatus);
        Assert.Equal(IncomingFollowUp.ReconciliationRequired, result.FollowUp);
        Assert.Equal((1, 1), (test.Cbs.Submissions, test.Cbs.Queries));
    }

    [Fact]
    public async Task Shutdown_during_the_call_leaves_a_marker_without_inventing_a_failure_or_decision()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterAsync();
        using var shutdown = new CancellationTokenSource();
        test.Cbs.Submit = token => { shutdown.Cancel(); return Task.FromCanceled<CoreResponse>(token); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.ProcessAsync(id, shutdown.Token));
        await using var db = test.Database.Context();
        var stored = (await new IncomingProcessingRepository(db).ReadAsync(id, default))!;
        Assert.Null(Assert.Single(stored.Calls).Completion);
        Assert.Null(stored.Payment.IpsDecision);
        Assert.Equal(0, test.Cbs.Queries);
    }

    [Fact]
    public async Task Stored_request_context_is_immutable_and_cancellation_before_ownership_has_no_effect()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterAsync(acceptance: Now.AddSeconds(-1));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.ProcessAsync(id, cancelled.Token));
        await using var db = test.Database.Context();
        var stored = (await new IncomingProcessingRepository(db).ReadAsync(id, default))!;
        Assert.Equal(Now.AddSeconds(19), stored.Context.DeadlineUtc);
        Assert.Empty(stored.Calls);
        db.Entry(stored.Payment).Property("ContextJson").CurrentValue = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => new UnitOfWork(db).SaveAsync());
    }

    [Fact]
    public async Task Call_evidence_requires_committed_ownership_and_cannot_be_replaced_or_consumed_before_commit()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterAsync();
        await using var db = test.Database.Context();
        var work = new IncomingPaymentWorkRepository(db);
        var unit = new UnitOfWork(db);
        var repository = new IncomingProcessingRepository(db);
        var payment = (await repository.ReadAsync(id, default))!.Payment;
        var claim = (await work.StageClaimAsync(id, Now, TimeSpan.FromSeconds(45), default))!;
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => repository.StageCallAsync(claim, CoreCallKind.Submission, Now, default));
        await unit.SaveAsync();
        payment.BeginSubmission(Now);
        var call = await repository.StageCallAsync(claim, CoreCallKind.Submission, Now, default);
        var response = new CoreCallCompletion(new(200, "{\"Status\":\"ACCP\"}", [new("X-Trace", "one"), new("X-Trace", "two")]), null, Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.StageCompletionAsync(claim, call.Id, response, Now, default));
        await unit.SaveAsync();
        await repository.StageCompletionAsync(claim, call.Id, response, Now, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.StageConsumptionAsync(claim, call.Id, Now, default));
        await unit.SaveAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.StageCompletionAsync(claim, call.Id, response, Now, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.StageCallAsync(claim, CoreCallKind.Submission, Now, default));
        await using var read = test.Database.Context();
        var stored = Assert.Single((await new IncomingProcessingRepository(read).ReadAsync(id, default))!.Calls);
        Assert.Equal(new[] { new CoreHeader("X-Trace", "one"), new CoreHeader("X-Trace", "two") }, stored.Completion!.Response!.Headers);
        Assert.Equal(response.Response!.Body, stored.Completion.Response.Body);
        Assert.False(stored.Consumed);
    }

    [Fact]
    public async Task Restart_after_credit_and_lost_reply_uses_status_while_the_frozen_deadline_allows_it()
    {
        await using var test = await Harness.CreateAsync();
        // An acceptance timestamp ahead of receipt is preserved by the protocol snapshot; its deadline remains frozen.
        var id = await test.RegisterAsync(acceptance: Now.AddMinutes(1));
        await Assert.ThrowsAsync<Crash>(() => test.ProcessAsync(id, default, new CrashAtSave(5)));
        Assert.Equal(1, test.Cbs.Credits);
        test.Time.Now = Now.AddSeconds(46);
        Assert.True((await test.ProcessAsync(id))!.IpsDecision!.Accepted);
        Assert.Equal((1, 1, 1), (test.Cbs.Submissions, test.Cbs.Queries, test.Cbs.Credits));
    }

    [Fact]
    public async Task A_marker_only_restart_queries_404_without_ever_submitting_the_payment()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterAsync(acceptance: Now.AddMinutes(1));
        await Assert.ThrowsAsync<Crash>(() => test.ProcessAsync(id, default, new CrashAfterCommit(2)));
        test.Time.Now = Now.AddSeconds(46);
        var result = (await test.ProcessAsync(id))!;
        Assert.Equal((0, 1), (test.Cbs.Submissions, test.Cbs.Queries));
        Assert.False(result.IpsDecision!.Accepted);
        Assert.Equal(IncomingFollowUp.ReconciliationRequired, result.FollowUp);
    }

    private sealed class OutsideTransaction(IIncomingCoreClient inner, TransactionDbContext db) : IIncomingCoreClient
    {
        public Task<CoreResponse> SubmitAsync(string participantBic, Pacs008Request request, CancellationToken token)
        {
            Assert.Null(db.Database.CurrentTransaction);
            return inner.SubmitAsync(participantBic, request, token);
        }
        public Task<CoreResponse> QueryAsync(string participantBic, string reference, CancellationToken token)
        {
            Assert.Null(db.Database.CurrentTransaction);
            return inner.QueryAsync(participantBic, reference, token);
        }
    }

    private sealed class Harness(SqlTestDatabase database) : IAsyncDisposable
    {
        public SqlTestDatabase Database { get; } = database;
        public Clock Time { get; } = new();
        public CbsSimulator Cbs { get; } = new();
        public IncomingProcessingOptions Options { get; set; } = new();
        public static async Task<Harness> CreateAsync() => new(await SqlTestDatabase.CreateAsync());

        public async Task<Guid> RegisterAsync(long sequence = 1, string reference = "E2E", DateTimeOffset? acceptance = null)
        {
            await using var db = Database.Context();
            var unit = new UnitOfWork(db);
            var receipt = await new InboundReceiptIntake(new InboundReceiptRepository(db), unit)
                .RegisterAsync(new("BAGAGE22", sequence, "pacs.008", "<fixture/>", false, Time.Now), default);
            var claim = (await new InboundWork(new InboundWorkRepository(db), unit, Time)
                .AcquireAsync(receipt.JournalId, Options.Ownership, default))!;
            var incoming = new IncomingPacs008(new Pacs008Request
            {
                EndToEndId = reference,
                Amount = 12m,
                Currency = "GEL",
                AcceptanceDateTime = acceptance
            },
                new("HEADER", "GROUP", reference, "TX", null, null, null, null, null, null));
            return (await new IncomingPaymentIntake(new IncomingPaymentRepository(db), new InboundWorkRepository(db), unit, Time, Options)
                .RegisterAsync(claim, incoming, default)).PaymentId!.Value;
        }

        public async Task<IncomingProcessingResult?> ProcessAsync(Guid id, CancellationToken token = default, params IInterceptor[] interceptors)
        {
            await using var db = Database.Context(interceptors);
            var result = await new IncomingPacs008Processing(new IncomingProcessingRepository(db), new IncomingPaymentWorkRepository(db),
                new UnitOfWork(db), new OutsideTransaction(Cbs, db), new IncomingCoreReplyInterpreter(), Options, Time).ProcessAsync(id, token);
            return result;
        }
        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = IncomingCbsProcessingTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CbsSimulator : IIncomingCoreClient
    {
        private readonly HashSet<string> _credits = [];
        public int Credits => _credits.Count;
        public int Submissions { get; private set; }
        public int Queries { get; private set; }
        public Func<CancellationToken, Task<CoreResponse>>? Submit { get; set; }
        public Func<CancellationToken, Task<CoreResponse>>? Query { get; set; }
        public Task<CoreResponse> SubmitAsync(string participantBic, Pacs008Request payment, CancellationToken cancellationToken)
        {
            Submissions++;
            if (Submit is not null) return Submit(cancellationToken);
            _credits.Add(payment.EndToEndId!);
            return Task.FromResult(new CoreResponse(200, "{\"Status\":\"ACCP\",\"CoreReference\":\"credit-1\"}"));
        }
        public Task<CoreResponse> QueryAsync(string participantBic, string endToEndId, CancellationToken cancellationToken)
        {
            Queries++;
            return Query?.Invoke(cancellationToken) ?? Task.FromResult(_credits.Contains(endToEndId)
                ? new CoreResponse(200, "{\"Status\":\"ACCP\"}") : new CoreResponse(404, ""));
        }
    }
}
