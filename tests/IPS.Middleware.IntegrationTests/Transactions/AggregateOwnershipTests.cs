using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Transactions;

public sealed class AggregateOwnershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task Competing_claims_write_the_parent_before_any_event_is_inserted()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await Intake(database, "claim");
        await using var first = database.Session();
        await using var second = database.Session();
        var a = Assert.IsType<OutgoingPayment>(await first.Payments.FindAsync(id, default));
        var b = Assert.IsType<OutgoingPayment>(await second.Payments.FindAsync(id, default));
        Assert.NotNull(first.Work.StageClaim(a, Now, Lease));
        Assert.NotNull(second.Work.StageClaim(b, Now, Lease));
        a.BeginSending(Now);
        b.BeginSending(Now);
        var results = await Task.WhenAll(TrySave(first), TrySave(second));
        Assert.Single(results.Where(r => r));
        Assert.Single(results.Where(r => !r));
        Assert.Equal(1, a.PendingEvents.Count + b.PendingEvents.Count);
        await using var read = database.Session();
        Assert.Equal(2, (await read.Payments.ReadEventsAsync(id, default)).Count);
    }

    [Fact]
    public async Task Concurrent_observations_are_version_checked_even_without_a_status_change()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await Intake(database, "observations");
        await using var first = database.Session();
        await using var second = database.Session();
        var a = Assert.IsType<OutgoingPayment>(await first.Payments.FindAsync(id, default));
        var b = Assert.IsType<OutgoingPayment>(await second.Payments.FindAsync(id, default));
        a.RecordStep(ProcessingStep.Validated, Now);
        b.RecordStep(ProcessingStep.XmlGenerated, Now);
        var results = await Task.WhenAll(TrySave(first), TrySave(second));
        Assert.Contains(true, results);
        Assert.Contains(false, results);
        await using var read = database.Session();
        Assert.Equal(2, (await read.Payments.ReadEventsAsync(id, default)).Count);
    }

    [Fact]
    public async Task Completion_releases_ownership_and_wrong_or_expired_tokens_are_refused()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await Intake(database, "complete");
        TransactionClaim claim;
        await using (var start = database.Session())
            claim = Assert.IsType<TransactionClaim>(await start.Processing(Now).TryStartAsync(id, Lease, default));
        await using (var invalid = database.Session())
        {
            var payment = Assert.IsType<OutgoingPayment>(await invalid.Payments.FindAsync(id, default));
            Assert.False(invalid.Work.StageCompletion(payment, claim with { Token = Guid.NewGuid() }, Now, null));
            Assert.False(invalid.Work.StageCompletion(payment, claim, Now.Add(Lease), null));
            Assert.Equal(0, await invalid.Unit.SaveAsync(default));
        }
        await using var completion = database.Session();
        var aggregate = Assert.IsType<OutgoingPayment>(await completion.Payments.FindAsync(id, default));
        aggregate.RecordAcceptance(StatusSource.Ips, Now.AddSeconds(1));
        Assert.True(completion.Work.StageCompletion(aggregate, claim, Now.AddSeconds(1), null));
        Assert.True(await completion.Unit.SaveAsync(default) > 0);
        aggregate.RecordAcceptance(StatusSource.Ips, Now.AddSeconds(2));
        Assert.True(await completion.Unit.SaveAsync(default) > 0);
    }

    [Fact]
    public async Task Ordinary_writes_cannot_bypass_claim_fencing()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await Intake(database, "fenced");
        await using (var start = database.Session())
            Assert.NotNull(await start.Processing(Now).TryStartAsync(id, Lease, default));
        await using var wrongOwner = database.Session();
        var payment = Assert.IsType<OutgoingPayment>(await wrongOwner.Payments.FindAsync(id, default));
        payment.RecordAcceptance(StatusSource.Ips, Now.AddSeconds(1));
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => wrongOwner.Unit.SaveAsync(default));
        Assert.Single(payment.PendingEvents);
        await using var read = database.Session();
        Assert.Equal(TransactionStatus.Sending, (await read.Payments.FindAsync(id, default))!.CurrentStatus);
    }

    [Fact]
    public async Task Recovery_and_old_completion_compete_atomically_and_the_loser_cannot_overwrite()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await Intake(database, "recovery-race");
        TransactionClaim claim;
        await using (var start = database.Session())
            claim = Assert.IsType<TransactionClaim>(await start.Processing(Now).TryStartAsync(id, Lease, default));
        await using var old = database.Session();
        await using var recovery = database.Session();
        var a = Assert.IsType<OutgoingPayment>(await old.Payments.FindAsync(id, default));
        var b = Assert.IsType<OutgoingPayment>(await recovery.Payments.FindAsync(id, default));
        a.RecordAcceptance(StatusSource.Ips, Now.AddSeconds(44));
        Assert.True(old.Work.StageCompletion(a, claim, Now.AddSeconds(44), null));
        b.MarkOutcomeUnknown(StatusSource.Recovery, Now.AddSeconds(45));
        Assert.True(recovery.Work.StageRecovery(b, Now.AddSeconds(45)));
        var results = await Task.WhenAll(TrySave(old), TrySave(recovery));
        Assert.Contains(true, results);
        Assert.Contains(false, results);
        await using var read = database.Session();
        var stored = Assert.IsType<OutgoingPayment>(await read.Payments.FindAsync(id, default));
        Assert.Contains(stored.CurrentStatus, new[] { TransactionStatus.Accepted, TransactionStatus.Uncertain });
        Assert.Equal(3, (await read.Payments.ReadEventsAsync(id, default)).Count);
        Assert.False(read.Work.StageCompletion(stored, claim, Now.AddSeconds(46), null));
    }

    [Fact]
    public async Task Expiry_recovery_marks_unknown_and_stale_token_cannot_complete_a_new_attempt()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await Intake(database, "expired");
        TransactionClaim claim;
        await using (var start = database.Session())
            claim = Assert.IsType<TransactionClaim>(await start.Processing(Now).TryStartAsync(id, Lease, default));
        await using (var early = database.Session())
        {
            Assert.Empty(await early.Work.FindExpiredAsync(Now.AddSeconds(44), 10, default));
            Assert.Equal(TransactionWorkResult.Unchanged, await early.Processing(Now.AddSeconds(44)).TryRecoverAsync(id, default));
        }
        await using (var recovery = database.Session())
        {
            Assert.Equal(new[] { id }, await recovery.Work.FindExpiredAsync(Now.AddSeconds(45), 10, default));
            Assert.Equal(TransactionWorkResult.Saved, await recovery.Processing(Now.AddSeconds(45)).TryRecoverAsync(id, default));
        }
        await using var next = database.Session();
        var payment = Assert.IsType<OutgoingPayment>(await next.Payments.FindAsync(id, default));
        Assert.Equal(TransactionStatus.Uncertain, payment.CurrentStatus);
        payment.BeginInvestigation(Now.AddSeconds(46));
        var newClaim = Assert.IsType<TransactionClaim>(next.Work.StageClaim(payment, Now.AddSeconds(46), Lease));
        Assert.True(await next.Unit.SaveAsync(default) > 0);
        Assert.NotEqual(claim.Token, newClaim.Token);
        Assert.False(next.Work.StageCompletion(payment, claim, Now.AddSeconds(47), null));
    }

    [Fact]
    public async Task Discovery_prioritizes_pacs008_and_respects_due_time_and_live_ownership()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var transfer = await Intake(database, "transfer", "pacs.009");
        var priority = await Intake(database, "priority");
        var deferred = await Intake(database, "deferred");
        var active = await Intake(database, "active");
        await using (var start = database.Session())
        {
            var claim = Assert.IsType<TransactionClaim>(await start.Processing(Now).TryStartAsync(deferred, Lease, default));
            var payment = Assert.IsType<OutgoingPayment>(await start.Payments.FindAsync(deferred, default));
            payment.ScheduleConnectionRetry(Now.AddSeconds(1));
            Assert.True(start.Work.StageCompletion(payment, claim, Now.AddSeconds(1), Now.AddSeconds(20)));
            Assert.True(await start.Unit.SaveAsync(default) > 0);
        }
        await using (var start = database.Session())
            Assert.NotNull(await start.Processing(Now).TryStartAsync(active, Lease, default));
        await using var discovery = database.Session();
        Assert.Equal(new[] { priority, transfer },
            await discovery.Work.FindDueAsync(TransactionStatus.Received, Now.AddSeconds(19), 10, default));
        Assert.Contains(deferred, await discovery.Work.FindDueAsync(TransactionStatus.Received, Now.AddSeconds(20), 10, default));
        Assert.Null(await discovery.Processing(Now.AddSeconds(19)).TryStartAsync(deferred, Lease, default));
        Assert.Null(await discovery.Processing(Now).TryStartAsync(active, Lease, default));
        Assert.Null(await discovery.Processing(Now).TryStartAsync(Guid.NewGuid(), Lease, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => discovery.Work.FindDueAsync(TransactionStatus.Accepted, Now, 1, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => discovery.Work.FindExpiredAsync(Now, 1001, default));
    }

    [Theory]
    [InlineData(TransactionStatus.Sending)]
    [InlineData(TransactionStatus.Investigating)]
    [InlineData(TransactionStatus.Resending)]
    public async Task Every_abandoned_inflight_state_recovers_to_uncertain(TransactionStatus state)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await Intake(database, "recover-" + state);
        await using (var start = database.Session())
        {
            var claim = Assert.IsType<TransactionClaim>(await start.Processing(Now).TryStartAsync(id, Lease, default));
            if (state != TransactionStatus.Sending)
            {
                var payment = Assert.IsType<OutgoingPayment>(await start.Payments.FindAsync(id, default));
                payment.MarkOutcomeUnknown(StatusSource.Gateway, Now.AddSeconds(1));
                Assert.True(start.Work.StageCompletion(payment, claim, Now.AddSeconds(1), null));
                Assert.True(await start.Unit.SaveAsync(default) > 0);
            }
        }
        if (state != TransactionStatus.Sending)
        {
            await using var next = database.Session();
            var payment = Assert.IsType<OutgoingPayment>(await next.Payments.FindAsync(id, default));
            Assert.NotNull(next.Work.StageClaim(payment, Now.AddSeconds(2), Lease));
            if (state == TransactionStatus.Investigating) payment.BeginInvestigation(Now.AddSeconds(2));
            else payment.BeginResending(StatusSource.Recovery, Now.AddSeconds(2));
            Assert.True(await next.Unit.SaveAsync(default) > 0);
        }
        await using var recovery = database.Session();
        Assert.Equal(TransactionWorkResult.Saved, await recovery.Processing(Now.AddSeconds(47)).TryRecoverAsync(id, default));
        await using var read = database.Session();
        var stored = Assert.IsType<OutgoingPayment>(await read.Payments.FindAsync(id, default));
        Assert.Equal(TransactionStatus.Uncertain, stored.CurrentStatus);
        Assert.Equal(StatusSource.Recovery, stored.CurrentSource);
        Assert.Equal(Now.AddSeconds(47), stored.CurrentStatusAtUtc);
        Assert.Null(read.Context.Entry(stored).Property<Guid?>("ClaimToken").CurrentValue);
        Assert.Contains(id, await read.Work.FindDueAsync(TransactionStatus.Uncertain, Now.AddSeconds(47), 10, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_completion_or_recovery_preserves_persisted_ownership_and_scheduling(bool recover)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await Intake(database, "release-rollback");
        TransactionClaim claim;
        await using (var start = database.Session())
            claim = Assert.IsType<TransactionClaim>(await start.Processing(Now).TryStartAsync(id, Lease, default));
        await using var change = database.Session();
        await change.Context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE TransactionEvents ADD CONSTRAINT CK_Test_OnlyStarted CHECK (Sequence <= 2)");
        var payment = Assert.IsType<OutgoingPayment>(await change.Payments.FindAsync(id, default));
        if (recover)
        {
            payment.MarkOutcomeUnknown(StatusSource.Recovery, Now.AddSeconds(45));
            Assert.True(change.Work.StageRecovery(payment, Now.AddSeconds(45)));
        }
        else
        {
            payment.RecordAcceptance(StatusSource.Ips, Now.AddSeconds(1));
            Assert.True(change.Work.StageCompletion(payment, claim, Now.AddSeconds(1), Now.AddSeconds(60)));
        }
        var pending = Assert.Single(payment.PendingEvents);
        await Assert.ThrowsAsync<DbUpdateException>(() => change.Unit.SaveAsync(default));
        Assert.Equal(pending, Assert.Single(payment.PendingEvents));
        await Assert.ThrowsAsync<InvalidOperationException>(() => change.Unit.SaveAsync(default));
        await using var read = database.Session();
        var persisted = Assert.IsType<OutgoingPayment>(await read.Payments.FindAsync(id, default));
        Assert.Equal(TransactionStatus.Sending, persisted.CurrentStatus);
        Assert.Equal(Now, persisted.CurrentStatusAtUtc);
        Assert.Equal(2, persisted.EventSequence);
        Assert.Equal(claim.Token, read.Context.Entry(persisted).Property<Guid?>("ClaimToken").CurrentValue);
        Assert.Equal(claim.ExpiresAtUtc, read.Context.Entry(persisted).Property<DateTimeOffset?>("ClaimExpiresAtUtc").CurrentValue);
        Assert.Null(read.Context.Entry(persisted).Property<DateTimeOffset?>("NextActionAtUtc").CurrentValue);
        Assert.Equal(2, (await read.Payments.ReadEventsAsync(id, default)).Count);
        Assert.Contains(id, await read.Work.FindExpiredAsync(Now.AddSeconds(45), 10, default));
    }

    private static async Task<bool> TrySave(PaymentSession session)
    {
        try { await session.Unit.SaveAsync(); return true; }
        catch (PersistenceConcurrencyException) { return false; }
    }

    private static async Task<Guid> Intake(SqlTestDatabase database, string reference, string type = "pacs.008")
    {
        await using var session = database.Session();
        return (await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate(type, reference, "{}").Request!, default)).Payment.Id;
    }
}
