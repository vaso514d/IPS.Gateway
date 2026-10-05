using System.Data.Common;
using System.Data.SqlTypes;
using System.Text.Json;
using System.Text.Json.Serialization;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Repositories.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using IPS.Middleware.Infrastructure.UnitOfWork;
using IPS.Middleware.IntegrationTests.Transactions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

public sealed class IncomingReconciliationTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    [Fact]
    public async Task Correlated_rejection_closes_unknown_without_changing_ips_decision()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync();
        var before = await test.ReadAsync(id);
        test.Remote.Query = _ => Task.FromResult(new CoreResponse(200, "{\"Status\":\"RJCT\",\"EndToEndId\":\"E2E\",\"ReasonCode\":\"AC01\"}"));
        var result = await test.RunAsync(id);
        Assert.Equal(CoreOutcome.Rejected, result!.CoreStatus);
        Assert.Equal(IncomingFollowUp.None, result.FollowUp);
        Assert.Equal(before.Payment.IpsDecision, result.IpsDecision);
        Assert.Null((await test.ReadAsync(id)).FollowUpAtUtc);
        Assert.Empty(await test.DiscoverAsync());
        Assert.Equal((1, 0), (test.Remote.Queries, test.Remote.Reversals));
    }

    [Theory]
    [InlineData(404, "")]
    [InlineData(500, "")]
    [InlineData(200, "{}")]
    [InlineData(200, "{\"Status\":\"PDNG\"}")]
    [InlineData(200, "not-json")]
    [InlineData(200, "{\"Status\":\"ACCP\",\"EndToEndId\":\"OTHER\"}")]
    public async Task Unresolved_evidence_retries_then_expires_without_resubmitting(int status, string body)
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync();
        test.Remote.Query = _ => Task.FromResult(new CoreResponse(status, body));
        foreach (var seconds in new[]
        {
            30,
            60,
            300,
            900
        }

        )
        {
            var now = test.Time.Now;
            var result = await test.RunAsync(id);
            Assert.Equal(IncomingFollowUp.ReconciliationRequired, result!.FollowUp);
            var stored = await test.ReadAsync(id);
            Assert.Equal(now.AddSeconds(seconds), stored.FollowUpAtUtc);
            Assert.All(stored.Calls, c => Assert.True(c.Consumed));
            Assert.Empty(await test.DiscoverAsync());
            test.Time.Now = stored.FollowUpAtUtc!.Value;
        }

        test.Time.Now = Start.AddHours(24);
        Assert.Equal(IncomingFollowUp.ManualReviewRequired, (await test.RunAsync(id))!.FollowUp);
        Assert.Equal(4, test.Remote.Queries);
        Assert.Equal(0, test.Remote.Reversals);
        Assert.Empty(await test.DiscoverAsync());
    }

    [Theory]
    [InlineData(200, ReversalDelivery.Accepted)]
    [InlineData(202, ReversalDelivery.Accepted)]
    [InlineData(500, ReversalDelivery.Unsuccessful)]
    [InlineData(400, ReversalDelivery.Unsuccessful)]
    public async Task Late_credit_reversal_preserves_frozen_notification_and_never_infers_completion(int status, ReversalDelivery delivery)
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync();
        test.Remote.Query = _ => Task.FromResult(new CoreResponse(200, "{\"Status\":\"ACCP\",\"CoreReference\":\"CBS-17\"}"));
        Assert.Equal(IncomingFollowUp.ReversalRequired, (await test.RunAsync(id))!.FollowUp);
        Assert.Equal(0, test.Remote.Reversals);
        test.Remote.Reverse = _ => Task.FromResult(new CoreResponse(status, "receipt", [new("Trace", "one"), new("Trace", "two")]));
        await test.RunAsync(id);
        var stored = await test.ReadAsync(id);
        Assert.Equal(CoreOutcome.Accepted, stored.Payment.CoreStatus);
        Assert.False(stored.Payment.IpsDecision!.Accepted);
        Assert.Equal(delivery, stored.Payment.Reversal);
        Assert.Equal(IncomingFollowUp.ManualReviewRequired, stored.Payment.FollowUp);
        var call = Assert.Single(stored.Calls.Where(c => c.Kind == CoreCallKind.Reversal));
        Assert.Equal(test.Remote.LastNotification, call.Notification);
        Assert.Equal(Start, call.Notification!.StatusAtUtc);
        Assert.Equal("MS03", call.Notification.ReasonCode);
        Assert.Equal(2, call.Completion!.Response!.Headers.Count);
        var json = JsonSerializer.Serialize(IncomingReversalContract.Map(call.Notification));
        using var contract = JsonDocument.Parse(json);
        Assert.Equal("E2E", contract.RootElement.GetProperty("coreReference").GetString());
        Assert.Equal("E2E", contract.RootElement.GetProperty("endToEndId").GetString());
        Assert.Equal("GROUP", contract.RootElement.GetProperty("messageId").GetString());
        Assert.Equal(id, contract.RootElement.GetProperty("transactionId").GetGuid());
        Assert.Equal("", contract.RootElement.GetProperty("clientReference").GetString());
        await test.RunAsync(id);
        Assert.Equal((1, 1), (test.Remote.Queries, test.Remote.Reversals));
        Assert.Empty(await test.DiscoverAsync());
    }

    [Fact]
    public async Task Lost_reversal_reply_does_not_repeat_remote_effect()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync(credit: true);
        test.Remote.Reverse = _ => throw new IOException("reply lost after reversal");
        await test.RunAsync(id);
        Assert.Equal(1, test.Remote.Reversals);
        Assert.Single(test.Remote.ReversedPayments);
        Assert.Equal(ReversalDelivery.Uncertain, (await test.ReadAsync(id)).Payment.Reversal);
        await test.RunAsync(id);
        Assert.Equal(1, test.Remote.Reversals);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    public async Task Restart_after_each_committed_checkpoint_preserves_evidence(bool reversal, int commit)
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync(credit: reversal);
        test.Remote.Query = _ => Task.FromResult(new CoreResponse(200, "{\"Status\":\"RJCT\"}"));
        await Assert.ThrowsAsync<Crash>(() => test.RunAsync(id, default, new AfterCommit(commit)));
        test.Time.Now += TimeSpan.FromSeconds(46);
        await test.RunAsync(id);
        var stored = await test.ReadAsync(id);
        if (reversal)
        {
            Assert.Equal(IncomingFollowUp.ManualReviewRequired, stored.Payment.FollowUp);
            Assert.Equal(commit == 2 ? ReversalDelivery.Uncertain : ReversalDelivery.Accepted, stored.Payment.Reversal);
            Assert.Equal(commit == 2 ? 0 : 1, test.Remote.Reversals);
        }
        else
        {
            Assert.Equal(CoreOutcome.Rejected, stored.Payment.CoreStatus);
            Assert.Equal(1, test.Remote.Queries);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_result_is_replayed_before_expiry(bool credit)
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync();
        test.Remote.Query = _ => Task.FromResult(new CoreResponse(200, credit ? "{\"Status\":\"ACCP\"}" : "{\"Status\":\"RJCT\"}"));
        await Assert.ThrowsAsync<Crash>(() => test.RunAsync(id, default, new AfterCommit(3)));
        test.Time.Now = Start.AddHours(25);
        var result = await test.RunAsync(id);
        Assert.Equal(credit ? CoreOutcome.Accepted : CoreOutcome.Rejected, result!.CoreStatus);
        Assert.Equal(credit ? IncomingFollowUp.ManualReviewRequired : IncomingFollowUp.None, result.FollowUp);
        Assert.Equal(1, test.Remote.Queries);
        Assert.Equal(0, test.Remote.Reversals);
    }

    [Fact]
    public async Task Concurrent_execution_and_expired_owner_cannot_commit_late_result()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<CoreResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Remote.Query = _ =>
        {
            entered.SetResult();
            return response.Task;
        };
        var first = test.RunAsync(id);
        await entered.Task;
        Assert.Empty(await test.DiscoverAsync());
        await test.RunAsync(id);
        Assert.Equal(1, test.Remote.Queries);
        test.Time.Now += TimeSpan.FromSeconds(46);
        test.Remote.Query = _ => Task.FromResult(new CoreResponse(200, "{\"Status\":\"RJCT\"}"));
        await test.RunAsync(id);
        response.SetResult(new(200, "{\"Status\":\"ACCP\"}"));
        await first;
        var stored = await test.ReadAsync(id);
        Assert.Equal(CoreOutcome.Rejected, stored.Payment.CoreStatus);
        Assert.Null(stored.Calls.First(c => c.Kind == CoreCallKind.Reconciliation).Completion);
    }

    [Fact]
    public async Task Complete_response_survives_service_cancellation_and_replays()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync(credit: true);
        using var stop = new CancellationTokenSource();
        test.Remote.Reverse = _ =>
        {
            stop.Cancel();
            return Task.FromResult(new CoreResponse(202, "accepted"));
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.RunAsync(id, stop.Token));
        Assert.NotNull((await test.ReadAsync(id)).Calls.Single(c => c.Kind == CoreCallKind.Reversal).Completion);
        test.Time.Now += TimeSpan.FromSeconds(46);
        await test.RunAsync(id);
        Assert.Equal(ReversalDelivery.Accepted, (await test.ReadAsync(id)).Payment.Reversal);
        Assert.Equal(1, test.Remote.Reversals);
    }

    [Fact]
    public async Task Cancellation_during_remote_call_leaves_only_marker_for_recovery()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync(credit: true);
        using var stop = new CancellationTokenSource();
        test.Remote.Reverse = async token =>
        {
            stop.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return new(202, "");
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.RunAsync(id, stop.Token));
        test.Time.Now += TimeSpan.FromSeconds(46);
        await test.RunAsync(id);
        Assert.Equal(ReversalDelivery.Uncertain, (await test.ReadAsync(id)).Payment.Reversal);
        Assert.Equal(1, test.Remote.Reversals);
    }

    [Fact]
    public async Task Timeout_is_uncertain_and_requires_manual_review()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync(credit: true);
        test.Options = new(callTimeout: TimeSpan.FromMilliseconds(40));
        test.Remote.Reverse = async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new(202, "");
        };
        await test.RunAsync(id);
        Assert.Equal(ReversalDelivery.Uncertain, (await test.ReadAsync(id)).Payment.Reversal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Failure_before_commit_rolls_back_and_recovers_without_duplicate_reversal(int commit)
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync(credit: true);
        await Assert.ThrowsAsync<Crash>(() => test.RunAsync(id, default, new BeforeCommit(commit)));
        var interrupted = await test.ReadAsync(id);
        Assert.Equal(IncomingFollowUp.ReversalRequired, interrupted.Payment.FollowUp);
        Assert.Equal(commit <= 2 ? ReversalDelivery.None : ReversalDelivery.Started, interrupted.Payment.Reversal);
        test.Time.Now += TimeSpan.FromSeconds(46);
        await test.RunAsync(id);
        var stored = await test.ReadAsync(id);
        Assert.Equal(commit == 3 ? ReversalDelivery.Uncertain : ReversalDelivery.Accepted, stored.Payment.Reversal);
        Assert.Equal(1, test.Remote.Reversals);
        Assert.Single(test.Remote.ReversedPayments);
        await using var db = test.Database.Context();
        var payloads = await db.Database.SqlQuery<string>($"SELECT PayloadJson AS Value FROM TransactionEvents WHERE TransactionId = {id} AND Name = 'incoming-payment.reconciliation-recorded' ORDER BY Sequence").ToListAsync();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };
        var history = payloads.Select(p => JsonSerializer.Deserialize<IncomingReconciliationRecorded>(p, json)!).ToArray();
        Assert.Equal(2, history.Length);
        Assert.Equal(ReversalDelivery.Started, history[0].Reversal);
        Assert.Equal(stored.Payment.Reversal, history[1].Reversal);
        Assert.Equal(stored.Payment.ManualReviewReason, history[1].ManualReviewReason);
        Assert.Equal(stored.Payment.IpsDecision, history[1].IpsDecision);
    }

    [Fact]
    public async Task Simultaneous_claims_have_one_committed_owner_and_expiry_is_exact()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync();
        await using var first = test.Database.Context();
        await using var second = test.Database.Context();
        var owner = (await new IncomingReconciliationRepository(first).StageClaimAsync(id, test.Time.Now, test.Options.Ownership, default))!;
        Assert.NotNull(await new IncomingReconciliationRepository(second).StageClaimAsync(id, test.Time.Now, test.Options.Ownership, default));
        await new UnitOfWork(first).SaveAsync();
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => new UnitOfWork(second).SaveAsync());
        test.Time.Now = owner.ExpiresAtUtc.AddTicks(-1);
        Assert.Empty(await test.DiscoverAsync());
        test.Time.Now = owner.ExpiresAtUtc;
        Assert.Equal(new[] { id }, await test.DiscoverAsync());
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => new IncomingReconciliationRepository(first).StageReleaseAsync(owner, test.Time.Now, test.Time.Now, default));
    }

    [Fact]
    public async Task Discovery_is_bounded_and_orders_by_due_time_then_registration_and_sql_id()
    {
        await using var test = await Harness.CreateAsync();
        var ids = new[]
        {
            await test.SeedAsync(reference: "A"),
            await test.SeedAsync(reference: "B"),
            await test.SeedAsync(reference: "C")
        };
        test.Options = new(discoveryBatch: 2);
        test.Time.Now = Start.AddSeconds(10).AddTicks(-1);
        Assert.Empty(await test.DiscoverAsync());
        test.Time.Now = Start.AddSeconds(10);
        Assert.Equal(ids.OrderBy(id => new SqlGuid(id)).Take(2), await test.DiscoverAsync());
        await using var db = test.Database.Context();
        var repo = new IncomingReconciliationRepository(db);
        var claim = (await repo.StageClaimAsync(ids[0], test.Time.Now, test.Options.Ownership, default))!;
        await new UnitOfWork(db).SaveAsync();
        await repo.StageReleaseAsync(claim, test.Time.Now, test.Time.Now.AddMinutes(1), default);
        await new UnitOfWork(db).SaveAsync();
        Assert.DoesNotContain(ids[0], await test.DiscoverAsync());
        test.Time.Now += TimeSpan.FromMinutes(1);
        Assert.Equal(ids.Skip(1).OrderBy(id => new SqlGuid(id)), await test.DiscoverAsync());
    }

    [Fact]
    public async Task Reversal_requires_committed_owner_and_frozen_payload_and_sql_enforces_one_marker()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync(credit: true);
        await using var db = test.Database.Context();
        var processing = new IncomingProcessingRepository(db);
        var snapshot = (await processing.ReadAsync(id, default))!;
        var repo = new IncomingReconciliationRepository(db);
        var claim = (await repo.StageClaimAsync(id, test.Time.Now, test.Options.Ownership, default))!;
        var notification = new ReversalNotification(id, "BAGAGE22", "E2E", Start, "MS03", snapshot.Payment.IpsDescription, "GROUP");
        snapshot.Payment.BeginReversal(test.Time.Now);
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => processing.StageCallAsync(claim, CoreCallKind.Reversal, test.Time.Now, default, notification));
        // Discard this scope: the uncommitted business change is intentionally not saved with the ownership acquisition.
        await using var owned = test.Database.Context();
        var ownRepo = new IncomingReconciliationRepository(owned);
        claim = (await ownRepo.StageClaimAsync(id, test.Time.Now, test.Options.Ownership, default))!;
        var unit = new UnitOfWork(owned);
        await unit.SaveAsync();
        processing = new(owned);
        snapshot = (await processing.ReadAsync(id, default))!;
        snapshot.Payment.BeginReversal(test.Time.Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => processing.StageCallAsync(claim, CoreCallKind.Reversal, test.Time.Now, default, new ReversalNotification(notification) { EndToEndId = "OTHER" }));
        var call = await processing.StageCallAsync(claim, CoreCallKind.Reversal, test.Time.Now, default, notification);
        await unit.SaveAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => processing.StageCallAsync(claim, CoreCallKind.Reversal, test.Time.Now, default, notification));
        await Assert.ThrowsAsync<SqlException>(() => owned.Database.ExecuteSqlAsync($"INSERT INTO IncomingCoreCalls (Id, PaymentId, Number, Kind, OwnerToken, StartedAtUtc, RequestJson, Consumed) SELECT NEWID(), PaymentId, Number + 1, Kind, OwnerToken, StartedAtUtc, RequestJson, 0 FROM IncomingCoreCalls WHERE Id = {call.Id}"));
        await ownRepo.StageReleaseAsync(claim, test.Time.Now, test.Time.Now, default);
        var evidence = owned.ChangeTracker.Entries().Single(e => e.Metadata.GetTableName() == "IncomingCoreCalls" && Equals(e.Property("Id").CurrentValue, call.Id));
        evidence.Property("RequestJson").CurrentValue = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.SaveAsync());
        Assert.Equal(notification, (await test.ReadAsync(id)).Calls.Single(c => c.Kind == CoreCallKind.Reversal).Notification);
    }

    [Fact]
    public async Task Remaining_window_clamps_the_remote_call_and_never_extends_retry_deadline()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync();
        test.Time.Now = Start.AddHours(24).AddMilliseconds(-40);
        test.Remote.Query = async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new(200, "");
        };
        await test.RunAsync(id).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(Start.AddHours(24), (await test.ReadAsync(id)).FollowUpAtUtc);
        test.Time.Now = Start.AddHours(24);
        Assert.Equal(IncomingFollowUp.ManualReviewRequired, (await test.RunAsync(id))!.FollowUp);
        Assert.Equal(1, test.Remote.Queries);
    }

    [Fact]
    public async Task Accepted_reversal_evidence_survives_restart_after_window_expiry()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync(credit: true);
        await Assert.ThrowsAsync<Crash>(() => test.RunAsync(id, default, new AfterCommit(3)));
        test.Time.Now = Start.AddHours(25);
        await test.RunAsync(id);
        var stored = await test.ReadAsync(id);
        Assert.Equal(ReversalDelivery.Accepted, stored.Payment.Reversal);
        Assert.Equal(IncomingFollowUp.ManualReviewRequired, stored.Payment.FollowUp);
        Assert.Equal(1, test.Remote.Reversals);
        Assert.Equal(0, test.Remote.Queries);
    }

    [Fact]
    public async Task Already_cancelled_run_leaves_work_due_without_creating_a_call()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.RunAsync(id, stop.Token));
        Assert.Single((await test.ReadAsync(id)).Calls);
        Assert.Equal(new[] { id }, await test.DiscoverAsync());
    }

    [Theory]
    [InlineData(48)]
    [InlineData(1)]
    public async Task Configuration_changes_do_not_move_an_existing_reconciliation_deadline(int configuredHours)
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.SeedAsync();
        test.Options = new(window: TimeSpan.FromHours(configuredHours));
        test.Time.Now = Start.AddHours(2);
        Assert.Equal(IncomingFollowUp.ReconciliationRequired, (await test.RunAsync(id))!.FollowUp);
        Assert.Equal(Start.AddHours(24), (await test.ReadAsync(id)).ReconciliationDeadlineUtc);
        test.Time.Now = Start.AddHours(24);
        Assert.Equal(IncomingFollowUp.ManualReviewRequired, (await test.RunAsync(id))!.FollowUp);
        Assert.Equal(1, test.Remote.Queries);
    }

    private sealed class Harness(SqlTestDatabase database) : IAsyncDisposable
    {
        public SqlTestDatabase Database { get; } = database;
        public Clock Time { get; } = new();
        public Simulator Remote { get; } = new();
        public IncomingReconciliationOptions Options { get; set; } = new();

        public static async Task<Harness> CreateAsync() => new(await SqlTestDatabase.CreateAsync());
        public async Task<Guid> SeedAsync(bool credit = false, string reference = "E2E")
        {
            await using var db = Database.Context();
            var unit = new UnitOfWork(db);
            var payment = IncomingPayment.Register(Guid.NewGuid(), "BAGAGE22", reference, Start);
            new IncomingPaymentRepository(db).Add(payment, new Pacs008Request { EndToEndId = reference, Amount = 12, Currency = "GEL" },
                new(Guid.NewGuid(), Start, Start.AddSeconds(20), new("HEADER", "GROUP", reference, "TX", null, null, null, null, null, null)));
            await unit.SaveAsync(default);
            var claim = (await new IncomingPaymentWorkRepository(db).StageClaimAsync(payment.Id, Start, TimeSpan.FromSeconds(45), default))!;
            await unit.SaveAsync(default);
            var repo = new IncomingProcessingRepository(db);
            payment.BeginSubmission(Start);
            var call = await repo.StageCallAsync(claim, CoreCallKind.Submission, Start, default);
            await unit.SaveAsync(default);
            await repo.StageCompletionAsync(claim, call.Id, new(new(200, credit ? "{\"Status\":\"ACCP\"}" : "{}"), null, Start), Start, default);
            await unit.SaveAsync(default);
            await repo.StageConsumptionAsync(claim, call.Id, Start, default);
            payment.RecordCoreResult(new(credit ? CoreOutcome.Accepted : CoreOutcome.Unknown, Start), Start);
            payment.DecideIps(false, Start);
            await repo.StageFinishAsync(claim, Start, Start.AddSeconds(10), Start + Options.Window, default);
            await unit.SaveAsync(default);
            return payment.Id;
        }

        public async Task<IncomingProcessingSnapshot> ReadAsync(Guid id)
        {
            await using var db = Database.Context();
            return (await new IncomingProcessingRepository(db).ReadAsync(id, default))!;
        }

        public async Task<IReadOnlyList<Guid>> DiscoverAsync()
        {
            await using var db = Database.Context();
            return await new IncomingReconciliationRepository(db).FindDueAsync(Time.Now, Options.DiscoveryBatch, default);
        }

        public async Task<IncomingProcessingResult?> RunAsync(Guid id, CancellationToken token = default, params IInterceptor[] interceptors)
        {
            await using var db = Database.Context(interceptors);
            Remote.CheckTransaction = () => Assert.Null(db.Database.CurrentTransaction);
            return await new IncomingReconciliation(new IncomingReconciliationRepository(db), new IncomingProcessingRepository(db),
                new UnitOfWork(db), Remote, Remote, new IncomingCoreReplyInterpreter(), Options, Time).ProcessAsync(id, token);
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = Start.AddSeconds(10);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Simulator : IIncomingCoreClient, IIncomingReversalClient
    {
        public int Queries { get; private set; }
        public int Reversals { get; private set; }
        public HashSet<Guid> ReversedPayments { get; } = [];
        public ReversalNotification? LastNotification { get; private set; }
        public Action? CheckTransaction { get; set; }
        public Func<CancellationToken, Task<CoreResponse>> Query { get; set; } = _ => Task.FromResult(new CoreResponse(404, ""));
        public Func<CancellationToken, Task<CoreResponse>> Reverse { get; set; } = _ => Task.FromResult(new CoreResponse(202, ""));

        public Task<CoreResponse> SubmitAsync(string participant, Pacs008Request request, CancellationToken token) => throw new Xunit.Sdk.XunitException("Reconciliation must never submit a payment.");
        public Task<CoreResponse> QueryAsync(string participant, string reference, CancellationToken token)
        {
            CheckTransaction?.Invoke();
            Assert.Equal("BAGAGE22", participant);
            Assert.Equal("E2E", reference);
            Queries++;
            return Query(token);
        }

        public Task<CoreResponse> RequestAsync(ReversalNotification notification, CancellationToken token)
        {
            CheckTransaction?.Invoke();
            LastNotification = notification;
            Reversals++;
            ReversedPayments.Add(notification.PaymentId);
            return Reverse(token);
        }
    }

    private sealed class Crash : Exception;
    private sealed class BeforeCommit(int target) : DbTransactionInterceptor
    {
        private int _commits;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (++_commits == target)
            {
                throw new Crash();
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class AfterCommit(int target) : DbTransactionInterceptor
    {
        private int _commits;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (++_commits == target)
            {
                throw new Crash();
            }

            return Task.CompletedTask;
        }
    }
}
