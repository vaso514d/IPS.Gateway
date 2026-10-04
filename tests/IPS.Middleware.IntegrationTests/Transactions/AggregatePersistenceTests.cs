using System.Text.Json;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Transactions;

public sealed class AggregatePersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Complete_migration_chain_can_be_recreated_on_a_fresh_database()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var db = database.Context();
        Assert.Equal(4, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.False(db.Database.HasPendingModelChanges());
        await db.GetService<IMigrator>().MigrateAsync("0");
        await db.Database.MigrateAsync();
        Assert.Equal(4, (await db.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task Materialization_reads_current_state_without_replaying_history_and_preserves_full_payloads()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        Guid id;
        await using (var session = database.Session())
        {
            var result = await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", " reference ", "{\"name\":\"საქართველო\"}").Request!, default);
            id = result.Payment.Id;
            result.Payment.BeginSending(Now.AddSeconds(1));
            result.Payment.RecordRejection(StatusSource.Ips, Now.AddSeconds(2), new(" ac01 ", 123, " invalid account "));
            result.Payment.RecordStep(ProcessingStep.Parsed, Now.AddSeconds(3));
            Assert.True(await session.Unit.SaveAsync(default) > 0);
            Assert.Empty(result.Payment.PendingEvents);
            Assert.Equal(0, await session.Unit.SaveAsync(default));
        }
        await using var read = database.Session();
        var payment = Assert.IsType<OutgoingPayment>(await read.Payments.FindAsync(id, default));
        Assert.Empty(payment.PendingEvents);
        Assert.Equal(TransactionStatus.Rejected, payment.CurrentStatus);
        Assert.Equal(Now.AddSeconds(2), payment.CurrentStatusAtUtc);
        Assert.Equal(3, payment.CurrentSequence);
        Assert.Equal(4, payment.EventSequence);
        Assert.Equal(new PaymentDetails("AC01", 123, "invalid account"), payment.Current.Details);
        Assert.Equal("{\"name\":\"საქართველო\"}", await read.Payments.ReadRequestAsync(id, default));
        var events = await read.Payments.ReadEventsAsync(id, default);
        Assert.Equal(new[] { 1, 2, 3, 4 }, events.Select(e => e.Sequence));
        Assert.Equal(4, events.Select(e => e.EventId).Distinct().Count());
        Assert.All(events, e => { Assert.Equal(1, e.SchemaVersion); Assert.Equal(id, e.TransactionId); });
        var rejected = events[2];
        Assert.Equal("payment.rejected", rejected.Name);
        using var payload = JsonDocument.Parse(rejected.PayloadJson);
        Assert.Equal(id, payload.RootElement.GetProperty("aggregateId").GetGuid());
        Assert.Equal(rejected.EventId, payload.RootElement.GetProperty("eventId").GetGuid());
        Assert.Equal("Rejected", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal("Sending", payload.RootElement.GetProperty("previousStatus").GetString());
        Assert.Equal(123, payload.RootElement.GetProperty("details").GetProperty("ipsInternalCode").GetInt32());
        Assert.Equal(Now.AddSeconds(2), payload.RootElement.GetProperty("occurredAtUtc").GetDateTimeOffset());

        // An unreadable historical payload must not participate in materializing authoritative current state.
        await read.Context.Database.ExecuteSqlRawAsync("UPDATE TransactionEvents SET PayloadJson = 'unknown-version' WHERE Sequence = 3");
        await using var another = database.Session();
        var reloaded = Assert.IsType<OutgoingPayment>(await another.Payments.FindAsync(id, default));
        Assert.Equal(payment.Current, reloaded.Current);
        Assert.Empty(reloaded.PendingEvents);
    }

    [Fact]
    public async Task Several_commits_append_each_pending_event_once()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var payment = (await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "multiple", "{}").Request!, default)).Payment;
        payment.BeginSending(Now);
        Assert.True(await session.Unit.SaveAsync(default) > 0);
        payment.RecordAcceptance(StatusSource.Ips, Now.AddSeconds(1), new(null, 10, "accepted"));
        Assert.True(await session.Unit.SaveAsync(default) > 0);
        payment.RecordAcceptance(StatusSource.Ips, Now.AddSeconds(2), new(null, 11, "repeated"));
        payment.RecordRejection(StatusSource.Ips, Now.AddSeconds(3), new("AC01", 12, "conflict"));
        Assert.True(await session.Unit.SaveAsync(default) > 0);
        Assert.Equal(0, await session.Unit.SaveAsync(default));
        Assert.Empty(payment.PendingEvents);
        var events = await session.Payments.ReadEventsAsync(payment.Id, default);
        Assert.Equal(5, events.Count);
        Assert.Equal("payment.outcome-observed", events[3].Name);
        Assert.Equal("payment.outcome-conflict-observed", events[4].Name);
        Assert.Equal(Now.AddSeconds(1), payment.CurrentStatusAtUtc);
        Assert.Equal(10, payment.CurrentIpsInternalCode);
    }

    [Fact]
    public async Task Concurrent_intake_is_idempotent_without_replacing_original_request()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var session = database.Session();
            return await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "same-reference", "{\"original\":true}").Request!, default);
        });
        var results = await Task.WhenAll(attempts);
        Assert.Single(results.Where(r => r.Created));
        Assert.Single(results.Select(r => r.Payment.Id).Distinct());
        await using var duplicate = database.Session();
        var result = await duplicate.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.009", " same-reference ", "{\"replace\":true}").Request!, default);
        Assert.False(result.Created);
        Assert.Equal("pacs.008", result.Payment.MessageType);
        Assert.Equal("{\"original\":true}", await duplicate.Payments.ReadRequestAsync(result.Payment.Id, default));
        Assert.Single(await duplicate.Payments.ReadEventsAsync(result.Payment.Id, default));
    }

    [Fact]
    public async Task Unrelated_primary_key_collision_is_not_reported_as_a_duplicate_reference()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var original = database.Session();
        var payment = (await original.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "original", "{}").Request!, default)).Payment;
        await using var collision = database.Session();
        var other = OutgoingPayment.Receive(payment.Id, "pacs.008", "different", Now);
        collision.Payments.Add(other, "{}");
        await Assert.ThrowsAsync<UniqueConstraintException>(() => collision.Unit.SaveAsync(default));
        Assert.Single(other.PendingEvents);
        await Assert.ThrowsAsync<InvalidOperationException>(() => collision.Unit.SaveAsync(default));
    }

    [Fact]
    public async Task Event_insert_failure_rolls_back_state_and_retains_pending_events()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var payment = (await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "rollback", "{}").Request!, default)).Payment;
        await session.Context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE TransactionEvents ADD CONSTRAINT CK_Test_OnlyIntake CHECK (Sequence = 1)");
        payment.BeginSending(Now);
        var eventId = Assert.Single(payment.PendingEvents).EventId;
        await Assert.ThrowsAsync<DbUpdateException>(() => session.Unit.SaveAsync(default));
        Assert.Equal(eventId, Assert.Single(payment.PendingEvents).EventId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Unit.SaveAsync(default));
        await using var read = database.Session();
        var persisted = Assert.IsType<OutgoingPayment>(await read.Payments.FindAsync(payment.Id, default));
        Assert.Equal(TransactionStatus.Received, persisted.CurrentStatus);
        Assert.Single(await read.Payments.ReadEventsAsync(payment.Id, default));
    }

    [Fact]
    public async Task Cancellation_between_parent_and_events_rolls_back_the_entire_commit()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        Guid id;
        await using (var intake = database.Session())
            id = (await intake.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "cancel", "{}").Request!, default)).Payment.Id;
        using var cancellation = new CancellationTokenSource();
        await using var session = database.Session(new CancelAfterParent(cancellation));
        var payment = Assert.IsType<OutgoingPayment>(await session.Payments.FindAsync(id, default));
        var claim = session.Work.StageClaim(payment, Now, TimeSpan.FromSeconds(45));
        Assert.NotNull(claim);
        payment.BeginSending(Now);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Unit.SaveAsync(cancellation.Token));
        Assert.Single(payment.PendingEvents);
        await using var read = database.Session();
        var stored = Assert.IsType<OutgoingPayment>(await read.Payments.FindAsync(id, default));
        Assert.Equal(TransactionStatus.Received, stored.CurrentStatus);
        Assert.NotNull(read.Work.StageClaim(stored, Now, TimeSpan.FromSeconds(45)));
        Assert.Single(await read.Payments.ReadEventsAsync(id, default));
    }

    [Fact]
    public async Task Direct_saves_and_history_mutation_are_rejected()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        Guid id;
        await using (var intake = database.Session())
            id = (await intake.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "immutable", "{}").Request!, default)).Payment.Id;
        await using (var direct = database.Session())
        {
            var payment = Assert.IsType<OutgoingPayment>(await direct.Payments.FindAsync(id, default));
            payment.BeginSending(Now);
            await Assert.ThrowsAsync<InvalidOperationException>(() => direct.Context.SaveChangesAsync());
        }
        await using var tamper = database.Session();
        var eventType = tamper.Context.Model.GetEntityTypes().Single(t => t.GetTableName() == "TransactionEvents").ClrType;
        var row = await tamper.Context.FindAsync(eventType, id, 1);
        Assert.NotNull(row);
        tamper.Context.Entry(row!).Property("PayloadJson").CurrentValue = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => tamper.Unit.SaveAsync(default));
    }

    [Fact]
    public async Task New_intake_event_failure_rolls_back_identity_and_request()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        await session.Context.Database.ExecuteSqlRawAsync("ALTER TABLE TransactionEvents ADD CONSTRAINT CK_Test_NoIntake CHECK (Sequence > 1)");
        var payment = OutgoingPayment.Receive(Guid.NewGuid(), "pacs.008", "failed-intake", Now);
        session.Payments.Add(payment, "{\"mustNotPersist\":true}");
        await Assert.ThrowsAsync<DbUpdateException>(() => session.Unit.SaveAsync(default));
        Assert.Single(payment.PendingEvents);
        await using var read = database.Session();
        Assert.Null(await read.Payments.FindAsync(payment.Id, default));
        Assert.Null(await read.Payments.ReadRequestAsync(payment.Id, default));
    }

    [Fact]
    public async Task Failed_claim_event_insert_rolls_back_claim_and_allows_a_fresh_owner()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var start = database.Session();
        var payment = (await start.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "claim-rollback", "{}").Request!, default)).Payment;
        await start.Context.Database.ExecuteSqlRawAsync("ALTER TABLE TransactionEvents ADD CONSTRAINT CK_Test_OnlyIntake CHECK (Sequence = 1)");
        Assert.NotNull(start.Work.StageClaim(payment, Now, TimeSpan.FromSeconds(45)));
        payment.BeginSending(Now);
        await Assert.ThrowsAsync<DbUpdateException>(() => start.Unit.SaveAsync(default));
        Assert.Single(payment.PendingEvents);
        await using var fresh = database.Session();
        await fresh.Context.Database.ExecuteSqlRawAsync("ALTER TABLE TransactionEvents DROP CONSTRAINT CK_Test_OnlyIntake");
        Assert.NotNull(await fresh.Processing(Now).TryStartAsync(payment.Id, TimeSpan.FromSeconds(45), default));
    }

    [Fact]
    public async Task Event_payloads_round_trip_all_immutable_domain_records()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var payment = OutgoingPayment.Receive(Guid.NewGuid(), "pacs.008", "payloads", Now);
        session.Payments.Add(payment, "{}");
        payment.BeginSending(Now);
        payment.RecordStep(ProcessingStep.Signed, Now.ToOffset(TimeSpan.FromHours(4)));
        payment.ScheduleConnectionRetry(Now, new("MS03", 10, "not connected"));
        payment.BeginSending(Now);
        payment.MarkOutcomeUnknown(StatusSource.Gateway, Now, new(description: "lost reply"));
        payment.BeginInvestigation(Now);
        payment.BeginResending(StatusSource.Investigation, Now);
        payment.RequireManualReview(Now);
        payment.ResolveManually(Now, new(description: "operator reconciled"));
        payment.RecordAcceptance(StatusSource.Ips, Now);
        payment.ResolveManually(Now);
        var expected = payment.PendingEvents.ToArray();
        Assert.True(await session.Unit.SaveAsync(default) > 0);
        var stored = await session.Payments.ReadEventsAsync(payment.Id, default);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        Assert.Equal(expected.Length, stored.Count);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.DoesNotContain("$type", stored[index].PayloadJson, StringComparison.Ordinal);
            var decoded = JsonSerializer.Deserialize(stored[index].PayloadJson, expected[index].GetType(), jsonOptions);
            Assert.Equal(expected[index], decoded);
            Assert.Equal(expected[index].EventId, stored[index].EventId);
            Assert.Equal(expected[index].OccurredAtUtc, stored[index].OccurredAtUtc);
        }
    }

    private sealed class CancelAfterParent(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return ValueTask.FromResult(result);
        }
    }
}
