using System.Data.Common;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.StatusDelivery;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using IPS.Middleware.IntegrationTests.Diagnostics;
using IPS.Middleware.IntegrationTests.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Payments;

[Collection("Metrics")]
public sealed class OutgoingStatusDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    [Theory]
    [InlineData(200)]
    [InlineData(204)]
    [InlineData(299)]
    public async Task Any_2xx_delivers_the_frozen_contract_once(int httpStatus)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var key = await FinalizeAsync(database);
        var remote = new Receiver((_, _) => Task.FromResult(httpStatus));
        Assert.Equal(StatusDeliveryResult.Delivered, await Deliver(database, key, remote, Now));
        Assert.Equal(StatusDeliveryResult.Unavailable, await Deliver(database, key, remote, Now));
        var payload = Assert.Single(remote.Sent);
        Assert.Equal("CBS-1:Accepted", payload.IdempotencyKey);
        var dto = OutgoingStatusContract.Map(payload);
        Assert.Equal(IPS.MiidleWear.Contracts.Transactions.TransactionStatus.Accepted, dto.Status);
        Assert.Equal(key.PaymentId, dto.TransactionId);
        Assert.NotNull(dto.MessageId);
        Assert.Null(dto.CoreReference);
        await using var read = database.Session();
        Assert.Empty(await new OutgoingStatusRepository(read.Context).FindDueAsync(Now.AddDays(1), 50, default));
    }

    // The deadlock graph of the 013 baseline: discovery held a delivery's index entry while it looked up the row a finishing
    // delivery held, and the finish then had to move that entry. Discovery now reads the claim from the index alone (013a).
    [Fact]
    public async Task Discovery_does_not_deadlock_with_a_delivery_that_holds_its_row_and_then_moves_its_index_entry()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var key = await FinalizeAsync(database);
        await using var writer = database.Context();
        // The service's database reads committed data with locks (EF Core creates its test databases with row versioning), and
        // holds thousands of delivered callbacks beside the pending one, so discovery seeks its index.
        await writer.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE");
        await writer.Database.ExecuteSqlAsync($"""
            INSERT INTO [OutgoingStatusDeliveries] ([PaymentId], [Sequence], [PayloadVersion], [PayloadJson], [State], [Attempts], [DeliveredAtUtc])
            SELECT [PaymentId], [Sequence] + [Number], 1, [PayloadJson], 1, 1, {Now}
            FROM [OutgoingStatusDeliveries]
            CROSS JOIN (SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS [Number] FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b) AS [Numbers]
            """);
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlAsync($"UPDATE [OutgoingStatusDeliveries] SET [LastFailure] = N'held' WHERE [PaymentId] = {key.PaymentId} AND [Sequence] = {key.Sequence}");
        var discovery = Task.Run(async () =>
        {
            await using var read = database.Session();
            return await new OutgoingStatusRepository(read.Context).FindDueAsync(Now, 50, default);
        });
        await Task.WhenAny(discovery, Task.Delay(TimeSpan.FromSeconds(1)));
        await writer.Database.ExecuteSqlAsync($"UPDATE [OutgoingStatusDeliveries] SET [NextAtUtc] = {Now.AddSeconds(5)} WHERE [PaymentId] = {key.PaymentId} AND [Sequence] = {key.Sequence}");
        await transaction.CommitAsync();
        Assert.Equal(new[] { key }, await discovery);
    }

    [Fact]
    public async Task Callback_results_are_counted_as_delivered_failed_or_exhausted()
    {
        using var probe = new MetricsProbe();
        await using var delivered = await SqlTestDatabase.CreateAsync();
        await Deliver(delivered, await FinalizeAsync(delivered), new Receiver((_, _) => Task.FromResult(200)), Now);
        await using var failing = await SqlTestDatabase.CreateAsync();
        var key = await FinalizeAsync(failing);
        var options = new StatusDeliveryOptions(attemptsPerRound: 2, maxRounds: 1);
        var remote = new Receiver((_, _) => Task.FromResult(503));

        await Deliver(failing, key, remote, Now, options);
        await Deliver(failing, key, remote, Now.AddSeconds(5), options);

        Assert.Equal(
            new Dictionary<string, long> { ["delivered"] = 1, ["failed"] = 1, ["exhausted"] = 1 },
            probe.CountBy("ips.cbs.callbacks", "result"));
    }

    [Fact]
    public async Task Retry_rounds_preserve_payload_and_exhaust_at_the_configured_boundary()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var key = await FinalizeAsync(database);
        var options = new StatusDeliveryOptions(attemptsPerRound: 2, maxRounds: 2);
        var remote = new Receiver((_, _) => Task.FromResult(503));
        var now = Now;
        Assert.Equal(StatusDeliveryResult.Scheduled, await Deliver(database, key, remote, now, options));
        Assert.Equal(StatusDeliveryResult.Unavailable, await Deliver(database, key, remote, now.AddSeconds(5).AddTicks(-1), options));
        now += TimeSpan.FromSeconds(5);
        Assert.Equal(StatusDeliveryResult.Scheduled, await Deliver(database, key, remote, now, options));
        now += TimeSpan.FromMinutes(10);
        Assert.Equal(StatusDeliveryResult.Scheduled, await Deliver(database, key, remote, now, options));
        now += TimeSpan.FromSeconds(5);
        Assert.Equal(StatusDeliveryResult.Exhausted, await Deliver(database, key, remote, now, options));
        Assert.Equal(StatusDeliveryResult.Unavailable, await Deliver(database, key, remote, now.AddDays(1), options));
        Assert.Equal(4, remote.Sent.Count);
        Assert.All(remote.Sent, status => Assert.Equivalent(remote.Sent[0], status, strict: true));
        await using var read = database.Session();
        var repository = new OutgoingStatusRepository(read.Context);
        Assert.NotNull(await new OutgoingStatusReader(repository, read.Unit, new Clock(now)).ReadAsync("pacs.008", "CBS-1", default));
        Assert.Equal(StatusDeliveryState.Exhausted, (await repository.ReadWorkAsync(key, default))!.State);
    }

    [Fact]
    public async Task Expired_marker_consumes_one_attempt_without_automatically_repeating_it()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var key = await FinalizeAsync(database);
        await using (var owner = database.Session())
        {
            Assert.NotNull(await new OutgoingStatusRepository(owner.Context).StageClaimAsync(key, Now, TimeSpan.FromSeconds(45), default));
            await owner.Unit.SaveAsync();
        }

        var remote = new Receiver((_, _) => Task.FromResult(200));
        Assert.Equal(StatusDeliveryResult.Scheduled, await Deliver(database, key, remote, Now.AddSeconds(45)));
        Assert.Empty(remote.Sent);
        Assert.Equal(StatusDeliveryResult.Unavailable, await Deliver(database, key, remote, Now.AddSeconds(50).AddTicks(-1)));
        Assert.Equal(StatusDeliveryResult.Delivered, await Deliver(database, key, remote, Now.AddSeconds(50)));
        Assert.Single(remote.Sent);
        await using var read = database.Session();
        Assert.Equal(2, (await new OutgoingStatusRepository(read.Context).ReadWorkAsync(key, default))!.Attempts);
    }

    // A repeated callback is allowed only after an unknown delivery outcome (013a decision 1); the record that judges it keeps
    // why the earlier attempt is unknown after the repeat delivered.
    [Fact]
    public async Task A_delivery_after_an_abandoned_attempt_keeps_the_unknown_outcome_on_record()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var key = await FinalizeAsync(database);
        await using (var owner = database.Session())
        {
            Assert.NotNull(await new OutgoingStatusRepository(owner.Context).StageClaimAsync(key, Now, TimeSpan.FromSeconds(45), default));
            await owner.Unit.SaveAsync();
        }

        var remote = new Receiver((_, _) => Task.FromResult(200));
        Assert.Equal(StatusDeliveryResult.Scheduled, await Deliver(database, key, remote, Now.AddSeconds(45)));
        Assert.Equal(StatusDeliveryResult.Delivered, await Deliver(database, key, remote, Now.AddSeconds(50)));
        await using var read = database.Session();
        var record = await read.Context.OutgoingStatusDeliveries.SingleAsync(x => x.PaymentId == key.PaymentId && x.Sequence == key.Sequence);
        Assert.Equal((StatusDeliveryState.Delivered, 2), (record.State, record.Attempts));
        Assert.Equal("Previous delivery owner expired; CBS receipt is unknown.", record.LastFailure);
    }

    [Fact]
    public async Task Competing_claims_have_one_committed_owner()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var key = await FinalizeAsync(database);
        await using var first = database.Session();
        await using var second = database.Session();
        Assert.NotNull(await new OutgoingStatusRepository(first.Context).StageClaimAsync(key, Now, TimeSpan.FromSeconds(45), default));
        Assert.NotNull(await new OutgoingStatusRepository(second.Context).StageClaimAsync(key, Now, TimeSpan.FromSeconds(45), default));
        await first.Unit.SaveAsync();
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => second.Unit.SaveAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.Unit.SaveAsync());
    }

    [Fact]
    public async Task Status_read_acknowledges_inflight_outcome_and_fences_callback_completion()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var key = await FinalizeAsync(database);
        var remote = new Receiver(async (_, _) =>
        {
            await using var read = database.Session();
            var result = await new OutgoingStatusReader(new OutgoingStatusRepository(read.Context), read.Unit, new Clock(Now))
                .ReadAsync("pacs.008", " CBS-1 ", default);
            Assert.Equal(key.Sequence, result!.Sequence);
            return 200;
        });
        Assert.Equal(StatusDeliveryResult.OwnershipLost, await Deliver(database, key, remote, Now));
        await using var verify = database.Session();
        Assert.Equal(StatusDeliveryState.Delivered, (await new OutgoingStatusRepository(verify.Context).ReadWorkAsync(key, default))!.State);
    }

    [Fact]
    public async Task New_manual_resolution_cannot_be_acknowledged_by_an_old_callback_or_read()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var old = await FinalizeAsync(database);
        OutgoingStatus? original;
        await using (var read = database.Session())
        {
            original = await new OutgoingStatusRepository(read.Context).ReadAsync("CBS-1", default);
        }

        var remote = new Receiver(async (_, _) =>
        {
            await using var change = database.Session();
            (await change.Payments.FindAsync(old.PaymentId, default))!.ResolveManually(Now.AddSeconds(1), new(description: "Operator correction"));
            await change.Unit.SaveAsync();
            return 200;
        });
        Assert.Equal(StatusDeliveryResult.OwnershipLost, await Deliver(database, old, remote, Now));
        await using var readCurrent = database.Session();
        var repository = new OutgoingStatusRepository(readCurrent.Context);
        Assert.False(await repository.StageAcknowledgeAsync(original!, Now.AddSeconds(2), default));
        var current = Assert.Single(await repository.FindDueAsync(Now.AddSeconds(2), 50, default));
        Assert.NotEqual(old.Sequence, current.Sequence);
        Assert.Equal(StatusDeliveryState.Pending, (await repository.ReadWorkAsync(current, default))!.State);
        Assert.Equal(2, await readCurrent.Context.Set<OutgoingStatusDeliveryRow>().CountAsync());
    }

    [Fact]
    public async Task Outcome_events_and_notification_rollback_together_and_observations_do_not_rearm()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        Guid id;
        await using (var setup = database.Session())
        {
            var payment = (await setup.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "CBS-1", "{}").Request!, default)).Payment;
            id = payment.Id;
            await setup.Context.Database.ExecuteSqlRawAsync("ALTER TABLE TransactionEvents ADD CONSTRAINT CK_Callback_Test CHECK (Sequence <= 1)");
            payment.RecordRejection(StatusSource.Ips, Now);
            await Assert.ThrowsAsync<DbUpdateException>(() => setup.Unit.SaveAsync());
        }

        await using (var fresh = database.Session())
        {
            Assert.Empty(await fresh.Context.Set<OutgoingStatusDeliveryRow>().ToListAsync());
            var payment = (await fresh.Payments.FindAsync(id, default))!;
            Assert.Equal(TransactionStatus.Received, payment.CurrentStatus);
            await fresh.Context.Database.ExecuteSqlRawAsync("ALTER TABLE TransactionEvents DROP CONSTRAINT CK_Callback_Test");
            payment.RecordRejection(StatusSource.Ips, Now);
            await fresh.Unit.SaveAsync();
            payment.RecordAcceptance(StatusSource.Ips, Now.AddSeconds(1));
            await fresh.Unit.SaveAsync();
            Assert.Single(await fresh.Context.Set<OutgoingStatusDeliveryRow>().ToListAsync());
        }
    }

    [Fact]
    public async Task Cancellation_and_lost_reply_keep_delivery_recoverable()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var key = await FinalizeAsync(database);
        using var stop = new CancellationTokenSource();
        var remote = new Receiver((_, token) =>
        {
            stop.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(200);
        });
        await using (var session = database.Session())
        {
            var delivery = new OutgoingStatusDelivery(
            new OutgoingStatusRepository(session.Context), remote, session.Unit, new(), new Clock(Now));
            Assert.Equal(StatusDeliveryResult.Scheduled, await delivery.DeliverAsync(key, stop.Token));
        }

        Assert.Equal(StatusDeliveryResult.Delivered, await Deliver(database, key, new Receiver((_, _) => Task.FromResult(200)), Now.AddSeconds(5)));
    }

    [Fact]
    public async Task Missing_mismatched_and_processing_reads_do_not_acknowledge_deliveries()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var key = await FinalizeAsync(database);
        await using var session = database.Session();
        var repository = new OutgoingStatusRepository(session.Context);
        var reader = new OutgoingStatusReader(repository, session.Unit, new Clock(Now));
        Assert.Null(await reader.ReadAsync("pacs.009", "CBS-1", default));
        Assert.Null(await reader.ReadAsync("pacs.008", "missing", default));
        Assert.Single(await repository.FindDueAsync(Now, 50, default));
        var payment = (await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "processing", "{}").Request!, default)).Payment;
        var processing = await reader.ReadAsync("pacs.008", payment.ClientReference, default);
        Assert.Equal(IPS.MiidleWear.Contracts.Transactions.TransactionStatus.Processing, OutgoingStatusContract.Map(processing!).Status);
        Assert.Single(await repository.FindDueAsync(Now, 50, default));
    }

    [Fact]
    public async Task Unlimited_rounds_remain_pending_and_manual_review_is_reportable()
    {
        var options = new StatusDeliveryOptions();
        Assert.Equal(StatusDeliveryState.Pending, options.AfterFailure(1000000, Now).State);
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var payment = (await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "manual", "{}").Request!, default)).Payment;
        payment.BeginSending(Now);
        payment.MarkOutcomeUnknown(StatusSource.Ips, Now);
        payment.RequireManualReview(Now);
        await session.Unit.SaveAsync();
        var repository = new OutgoingStatusRepository(session.Context);
        var key = Assert.Single(await repository.FindDueAsync(Now, 50, default));
        var status = (await repository.ReadWorkAsync(key, default))!.Status;
        Assert.Equal("manual:ManualReview", status.IdempotencyKey);
        Assert.Equal(IPS.MiidleWear.Contracts.Transactions.TransactionStatus.ManualReview, OutgoingStatusContract.Map(status).Status);
    }

    [Fact]
    public async Task Cancellation_before_claim_does_not_send_or_consume_an_attempt()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var key = await FinalizeAsync(database);
        var remote = new Receiver((_, _) => Task.FromResult(200));
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await using var session = database.Session();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OutgoingStatusDelivery(
            new OutgoingStatusRepository(session.Context), remote, session.Unit, new(), new Clock(Now)).DeliverAsync(key, stop.Token));
        Assert.Empty(remote.Sent);
        await using var read = database.Session();
        Assert.Equal(0, (await new OutgoingStatusRepository(read.Context).ReadWorkAsync(key, default))!.Attempts);
    }

    [Fact]
    public async Task Multiple_outcomes_in_one_commit_preserve_each_snapshot_but_only_dispatch_the_latest()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var payment = (await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "CBS-1", "{}").Request!, default)).Payment;
        payment.BeginSending(Now);
        payment.RecordAcceptance(StatusSource.Ips, Now, new(description: "Accepted by IPS"));
        var acceptedSequence = payment.CurrentSequence;
        payment.ResolveManually(Now.AddSeconds(1), new(description: "Operator correction"));
        await session.Unit.SaveAsync();
        await session.Unit.SaveAsync();
        await using var read = database.Session();
        var rows = await read.Context.Set<OutgoingStatusDeliveryRow>().OrderBy(r => r.Sequence).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(acceptedSequence, rows[0].Sequence);
        Assert.Equal(TransactionStatus.Accepted, rows[0].Payload().Status);
        Assert.Equal(Now, rows[0].Payload().StatusAtUtc);
        Assert.Equal("Accepted by IPS", rows[0].Payload().Details.Description);
        Assert.Equal(TransactionStatus.ManuallyResolved, rows[1].Payload().Status);
        Assert.Equal(Now.AddSeconds(1), rows[1].Payload().StatusAtUtc);
        Assert.Equal("Operator correction", rows[1].Payload().Details.Description);
        var repository = new OutgoingStatusRepository(read.Context);
        Assert.Equal(new StatusDeliveryKey(payment.Id, payment.CurrentSequence), Assert.Single(await repository.FindDueAsync(Now.AddSeconds(2), 50, default)));
        Assert.Null(await repository.StageClaimAsync(new(payment.Id, acceptedSequence), Now.AddSeconds(2), TimeSpan.FromSeconds(45), default));
    }

    [Fact]
    public async Task Outcome_changed_between_delivery_and_parent_reads_cannot_claim_the_old_snapshot()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var key = await FinalizeAsync(database);
        var race = new BeforeParentRead(async () =>
        {
            await using var change = database.Session();
            (await change.Payments.FindAsync(key.PaymentId, default))!.ResolveManually(Now.AddSeconds(1), new(description: "Operator correction"));
            await change.Unit.SaveAsync();
        });
        await using var session = database.Session(race);
        Assert.Null(await new OutgoingStatusRepository(session.Context).StageClaimAsync(key, Now.AddSeconds(2), TimeSpan.FromSeconds(45), default));
        Assert.True(race.Triggered);
        await session.Unit.SaveAsync();
        await using var verify = database.Session();
        var old = await verify.Context.Set<OutgoingStatusDeliveryRow>().SingleAsync(r => r.PaymentId == key.PaymentId && r.Sequence == key.Sequence);
        Assert.Equal(0, old.Attempts);
        Assert.Null(old.ClaimToken);
        Assert.NotEqual(key, Assert.Single(await new OutgoingStatusRepository(verify.Context).FindDueAsync(Now.AddSeconds(2), 50, default)));
    }

    private sealed class BeforeParentRead(Func<Task> change) : DbCommandInterceptor
    {
        public bool Triggered { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!Triggered && command.CommandText.Contains("FROM [Transactions]", StringComparison.Ordinal) &&
                !command.CommandText.Contains("[OutgoingStatusDeliveries]", StringComparison.Ordinal))
            {
                Triggered = true;
                await change();
            }

            return result;
        }
    }

    private static async Task<StatusDeliveryKey> FinalizeAsync(SqlTestDatabase database)
    {
        await using var session = database.Session();
        var payment = (await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "CBS-1", "{}").Request!, default)).Payment;
        payment.BeginSending(Now);
        payment.RecordAcceptance(StatusSource.Ips, Now);
        await session.Unit.SaveAsync();
        return new(payment.Id, payment.CurrentSequence);
    }

    private static async Task<StatusDeliveryResult> Deliver(SqlTestDatabase database, StatusDeliveryKey key, Receiver remote, DateTimeOffset now, StatusDeliveryOptions? options = null)
    {
        await using var session = database.Session();
        return await new OutgoingStatusDelivery(new OutgoingStatusRepository(session.Context), remote, session.Unit, options ?? new(), new Clock(now)).DeliverAsync(key, default);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Receiver(Func<OutgoingStatus, CancellationToken, Task<int>> send) : IOutgoingStatusReceiver
    {
        public List<OutgoingStatus> Sent { get; } = [];

        public Task<int> SendAsync(OutgoingStatus status, string idempotencyKey, CancellationToken cancellationToken)
        {
            Assert.Equal(status.IdempotencyKey, idempotencyKey);
            Sent.Add(status);
            return send(status, cancellationToken);
        }
    }
}
