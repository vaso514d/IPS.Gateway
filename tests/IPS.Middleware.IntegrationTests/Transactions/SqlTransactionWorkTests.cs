using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Transactions;

public sealed class SqlTransactionWorkTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(35);
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task Discovery_prioritizes_pacs008_then_oldest_other_types_and_bounds_the_batch()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var olderOther = await AddAsync(database, "pacs.009", Now.AddMinutes(-3));
        var newerOther = await AddAsync(database, "pacs.004", Now.AddMinutes(-2));
        var priority = await AddAsync(database, "pacs.008", Now.AddMinutes(-1));

        Assert.Equal(new[] { priority, olderOther }, await database.Store.FindDueAsync(TransactionStatus.Received, Now, 2, None));
        Assert.Equal(new[] { priority, olderOther, newerOther }, await database.Store.FindDueAsync(TransactionStatus.Received, Now, 5, None));
        Assert.Empty(await database.Store.FindDueAsync(TransactionStatus.Uncertain, Now, 5, None));
    }

    [Fact]
    public async Task Discovery_breaks_equal_time_ties_consistently()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await AddAsync(database);
        await AddAsync(database);
        var first = await database.Store.FindDueAsync(TransactionStatus.Received, Now, 1, None);
        var all = await database.Store.FindDueAsync(TransactionStatus.Received, Now, 2, None);
        Assert.Equal(first[0], all[0]);
        Assert.Equal(all, await database.Store.FindDueAsync(TransactionStatus.Received, Now, 2, None));
    }

    [Fact]
    public async Task Application_claims_only_received_work_and_preserves_history()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await AddAsync(database);
        var work = Work(database, Now);
        var claim = await work.TryStartAsync(id, Duration, None);
        Assert.NotNull(claim);
        Assert.Equal(id, claim.TransactionId);
        Assert.NotEqual(Guid.Empty, claim.Token);
        Assert.Equal(Now + Duration, claim.ExpiresAtUtc);
        var transaction = await database.Store.FindAsync(id, None);
        Assert.NotNull(transaction);
        Assert.Equal(TransactionStatus.Sending, transaction.Current.Status);
        Assert.Equal(StatusSource.Gateway, transaction.Current.Source);
        Assert.Equal(Now, transaction.Current.AtUtc);
        Assert.Equal(2, transaction.History.Count);
        Assert.Empty(await database.Store.FindDueAsync(TransactionStatus.Received, Now, 10, None));
        Assert.Null(await work.TryStartAsync(id, Duration, None));
        Assert.Null(await work.TryStartAsync(Guid.NewGuid(), Duration, None));

        var accepted = await AddAsync(database);
        await database.Store.TryUpdateAsync(accepted, Change(TransactionStatus.Accepted, Now), None);
        Assert.Null(await work.TryStartAsync(accepted, Duration, None));
    }

    [Fact]
    public async Task Two_decisions_on_the_same_version_have_one_claim_winner()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await AddAsync(database);
        using var decisions = new Barrier(2);
        var calls = 0;
        bool Start(PaymentTransaction transaction)
        {
            Interlocked.Increment(ref calls);
            Assert.True(decisions.SignalAndWait(TimeSpan.FromSeconds(15)));
            transaction.ChangeStatus(TransactionStatus.Sending, StatusSource.Gateway, Now);
            return true;
        }
        var claims = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
            database.Store.TryClaimAsync(id, Now, Duration, Start, None)))).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(claims.Where(claim => claim is not null));
        Assert.Equal(2, calls);
        Assert.Equal(2, (await database.Store.FindAsync(id, None))!.History.Count);
    }

    [Fact]
    public async Task Unfenced_updates_cannot_bypass_live_or_expired_ownership()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await AddAsync(database);
        await Work(database, Now).TryStartAsync(id, Duration, None);
        var calls = 0;
        Assert.Equal(TransactionUpdateResult.Conflict, await database.Store.TryUpdateAsync(id, transaction =>
        {
            calls++;
            return Change(TransactionStatus.Accepted, Now)(transaction);
        }, None));
        Assert.Equal(0, calls);
        Assert.Equal(new[] { id }, await database.Store.FindExpiredAsync(Now + Duration, 10, None));
        Assert.Equal(TransactionUpdateResult.Conflict, await database.Store.TryUpdateAsync(id, Change(TransactionStatus.Accepted, Now + Duration), None));
    }

    [Fact]
    public async Task Completion_releases_ownership_and_delayed_retry_becomes_due_at_the_boundary()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await AddAsync(database);
        var claim = (await Work(database, Now).TryStartAsync(id, Duration, None))!;
        var due = Now.AddMinutes(1).ToOffset(TimeSpan.FromHours(4));
        Assert.Equal(TransactionUpdateResult.Saved, await database.Store.TryCompleteAsync(
            claim, Now.AddSeconds(1), Change(TransactionStatus.Received, Now.AddSeconds(1)), due, None));
        Assert.Empty(await database.Store.FindDueAsync(TransactionStatus.Received, due.AddTicks(-1), 10, None));
        Assert.Null(await Work(database, due.AddTicks(-1)).TryStartAsync(id, Duration, None));
        Assert.Equal(new[] { id }, await database.Store.FindDueAsync(TransactionStatus.Received, due, 10, None));
        Assert.NotNull(await Work(database, due).TryStartAsync(id, Duration, None));
        Assert.Equal(TransactionUpdateResult.Conflict, await database.Store.TryCompleteAsync(
            claim, due, Change(TransactionStatus.Accepted, due), null, None));
        Assert.Equal(4, (await database.Store.FindAsync(id, None))!.History.Count);
    }

    [Theory]
    [InlineData(TransactionStatus.Sending)]
    [InlineData(TransactionStatus.Investigating)]
    [InlineData(TransactionStatus.Resending)]
    public async Task Fresh_store_recovers_expired_work_as_uncertain_and_fences_the_previous_worker(TransactionStatus inFlight)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await AddAsync(database);
        var claim = (await database.Store.TryClaimAsync(id, Now, Duration, Change(inFlight, Now), None))!;
        var replacement = new SqlTransactionStore(database.CreateFactory());
        Assert.Empty(await replacement.FindExpiredAsync(Now + Duration - TimeSpan.FromTicks(1), 10, None));
        Assert.Equal(TransactionUpdateResult.Unchanged, await new OutgoingTransactionWork(replacement, new FixedTime(Now + Duration - TimeSpan.FromTicks(1)))
            .TryRecoverAsync(id, None));
        Assert.Equal(new[] { id }, await replacement.FindExpiredAsync(Now + Duration, 10, None));
        Assert.Equal(TransactionUpdateResult.Saved, await new OutgoingTransactionWork(replacement, new FixedTime(Now + Duration)).TryRecoverAsync(id, None));

        var transaction = (await replacement.FindAsync(id, None))!;
        Assert.Equal(TransactionStatus.Uncertain, transaction.Current.Status);
        Assert.Equal(StatusSource.Recovery, transaction.Current.Source);
        Assert.Contains(inFlight.ToString(), transaction.Current.Description!);
        Assert.Equal(3, transaction.History.Count);
        Assert.Empty(await replacement.FindDueAsync(TransactionStatus.Received, Now + Duration, 10, None));
        Assert.Equal(new[] { id }, await replacement.FindDueAsync(TransactionStatus.Uncertain, Now + Duration, 10, None));
        Assert.Equal(TransactionUpdateResult.Conflict, await replacement.TryCompleteAsync(claim, Now + Duration,
            Change(TransactionStatus.Accepted, Now + Duration), null, None));
        Assert.Equal(TransactionUpdateResult.Unchanged, await Work(database, Now + Duration).TryRecoverAsync(id, None));

        var newClaim = await replacement.TryClaimAsync(id, Now + Duration, Duration, Change(TransactionStatus.Investigating, Now + Duration), None);
        Assert.NotNull(newClaim);
        Assert.NotEqual(claim.Token, newClaim.Token);
        Assert.Equal(TransactionUpdateResult.Conflict, await replacement.TryCompleteAsync(claim, Now + Duration,
            Change(TransactionStatus.Accepted, Now + Duration), null, None));
    }

    [Fact]
    public async Task Expired_and_wrong_tokens_are_refused_without_invoking_a_completion_decision()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await AddAsync(database);
        var claim = (await Work(database, Now).TryStartAsync(id, Duration, None))!;
        var called = false;
        bool Complete(PaymentTransaction transaction) { called = true; return true; }
        Assert.Equal(TransactionUpdateResult.Conflict, await database.Store.TryCompleteAsync(claim with { Token = Guid.NewGuid() }, Now, Complete, null, None));
        Assert.Equal(TransactionUpdateResult.Conflict, await database.Store.TryCompleteAsync(claim, Now + Duration, Complete, null, None));
        Assert.False(called);
        Assert.Equal(TransactionStatus.Sending, (await database.Store.FindAsync(id, None))!.Current.Status);
    }

    [Fact]
    public async Task Recovery_and_an_earlier_completion_snapshot_cannot_both_commit()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await AddAsync(database);
        var claim = (await Work(database, Now).TryStartAsync(id, Duration, None))!;
        using var decisions = new Barrier(2);
        bool Decide(PaymentTransaction transaction, TransactionStatus status, DateTimeOffset at)
        {
            Assert.True(decisions.SignalAndWait(TimeSpan.FromSeconds(15)));
            return Change(status, at)(transaction);
        }
        var completion = Task.Run(() => database.Store.TryCompleteAsync(claim, Now.AddSeconds(1),
            transaction => Decide(transaction, TransactionStatus.Accepted, Now.AddSeconds(1)), null, None));
        var recovery = Task.Run(() => database.Store.TryRecoverAsync(id, Now + Duration,
            transaction => Decide(transaction, TransactionStatus.Uncertain, Now + Duration), None));
        var results = await Task.WhenAll(completion, recovery).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results.Where(result => result == TransactionUpdateResult.Saved));
        Assert.Single(results.Where(result => result == TransactionUpdateResult.Conflict));
        Assert.Equal(3, (await database.Store.FindAsync(id, None))!.History.Count);
    }

    [Fact]
    public async Task Failure_appending_claim_history_rolls_back_ownership_and_state()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await AddAsync(database);
        await using var db = await database.Factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [TransactionHistory] ADD CONSTRAINT [RejectClaimHistory] CHECK ([Sequence] = 1)");
        await Assert.ThrowsAsync<DbUpdateException>(() => Work(database, Now).TryStartAsync(id, Duration, None));
        Assert.Single((await database.Store.FindAsync(id, None))!.History);
        Assert.Equal(new[] { id }, await database.Store.FindDueAsync(TransactionStatus.Received, Now, 10, None));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [TransactionHistory] DROP CONSTRAINT [RejectClaimHistory]");
        Assert.NotNull(await Work(database, Now).TryStartAsync(id, Duration, None));
    }

    [Fact]
    public async Task Failed_completion_and_recovery_retain_the_claim_and_original_history()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await AddAsync(database);
        var claim = (await Work(database, Now).TryStartAsync(id, Duration, None))!;
        await using var db = await database.Factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [TransactionHistory] ADD CONSTRAINT [RejectFurtherHistory] CHECK ([Sequence] <= 2)");
        await Assert.ThrowsAsync<DbUpdateException>(() => database.Store.TryCompleteAsync(
            claim, Now.AddSeconds(1), Change(TransactionStatus.Accepted, Now.AddSeconds(1)), null, None));
        await Assert.ThrowsAsync<DbUpdateException>(() => Work(database, Now + Duration).TryRecoverAsync(id, None));
        Assert.Equal(2, (await database.Store.FindAsync(id, None))!.History.Count);
        Assert.Equal(new[] { id }, await database.Store.FindExpiredAsync(Now + Duration, 10, None));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [TransactionHistory] DROP CONSTRAINT [RejectFurtherHistory]");
        Assert.Equal(TransactionUpdateResult.Saved, await Work(database, Now + Duration).TryRecoverAsync(id, None));
    }

    [Fact]
    public async Task Declined_noop_and_thrown_decisions_do_not_release_or_create_ownership()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await AddAsync(database);
        Assert.Null(await database.Store.TryClaimAsync(id, Now, Duration, Change(TransactionStatus.Sending, Now, false), None));
        Assert.Null(await database.Store.TryClaimAsync(id, Now, Duration, _ => true, None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Store.TryClaimAsync(id, Now, Duration,
            _ => throw new InvalidOperationException("decision failed"), None));
        var claim = (await Work(database, Now).TryStartAsync(id, Duration, None))!;
        Assert.Equal(TransactionUpdateResult.Unchanged, await database.Store.TryCompleteAsync(claim, Now,
            Change(TransactionStatus.Accepted, Now, false), null, None));
        Assert.Equal(TransactionUpdateResult.Unchanged, await database.Store.TryCompleteAsync(claim, Now, _ => true, null, None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Store.TryCompleteAsync(claim, Now,
            _ => throw new InvalidOperationException("decision failed"), null, None));
        Assert.Equal(TransactionUpdateResult.Unchanged, await database.Store.TryRecoverAsync(id, Now + Duration, _ => true, None));
        Assert.Equal(2, (await database.Store.FindAsync(id, None))!.History.Count);
        Assert.Equal(TransactionUpdateResult.Saved, await database.Store.TryCompleteAsync(claim, Now,
            Change(TransactionStatus.Accepted, Now), null, None));
        Assert.Empty(await database.Store.FindDueAsync(TransactionStatus.Received, Now, 10, None));
        Assert.Empty(await database.Store.FindExpiredAsync(Now + Duration, 10, None));
    }

    [Fact]
    public async Task Cancelled_claim_or_recovery_leaves_persisted_work_available()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await AddAsync(database);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Work(database, Now).TryStartAsync(id, Duration, cancellation.Token));
        var claim = (await Work(database, Now).TryStartAsync(id, Duration, None))!;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => database.Store.TryCompleteAsync(claim, Now,
            Change(TransactionStatus.Accepted, Now), null, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Work(database, Now + Duration).TryRecoverAsync(id, cancellation.Token));
        Assert.Equal(2, (await database.Store.FindAsync(id, None))!.History.Count);
        Assert.Equal(TransactionUpdateResult.Saved, await Work(database, Now + Duration).TryRecoverAsync(id, None));
    }

    [Fact]
    public async Task Invalid_arguments_and_missing_identity_have_explicit_results()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var store = database.Store;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.FindDueAsync(TransactionStatus.Accepted, Now, 10, None));
        foreach (var take in new[] { 0, -1, 1001 })
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.FindDueAsync(TransactionStatus.Received, Now, take, None));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.FindExpiredAsync(Now, take, None));
        }
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Work(database, Now).TryStartAsync(Guid.NewGuid(), TimeSpan.Zero, None));
        await Assert.ThrowsAsync<ArgumentException>(() => Work(database, Now).TryStartAsync(Guid.Empty, Duration, None));
        await Assert.ThrowsAsync<ArgumentException>(() => store.TryCompleteAsync(new(Guid.NewGuid(), Guid.Empty, Now), Now, _ => true, null, None));
        Assert.Equal(TransactionUpdateResult.NotFound, await store.TryCompleteAsync(new(Guid.NewGuid(), Guid.NewGuid(), Now + Duration), Now, _ => true, null, None));
        Assert.Equal(TransactionUpdateResult.NotFound, await Work(database, Now).TryRecoverAsync(Guid.NewGuid(), None));
    }


    [Fact]
    public async Task Cancellation_after_parent_write_rolls_back_claim_and_history()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await AddAsync(database);
        using var cancellation = new CancellationTokenSource();
        var store = new SqlTransactionStore(database.CreateFactory(new CancelAfterSave(cancellation)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new OutgoingTransactionWork(store, new FixedTime(Now)).TryStartAsync(id, Duration, cancellation.Token));
        Assert.Single((await database.Store.FindAsync(id, None))!.History);
        Assert.Equal(new[] { id }, await database.Store.FindDueAsync(TransactionStatus.Received, Now, 10, None));
        Assert.NotNull(await Work(database, Now).TryStartAsync(id, Duration, None));
    }

    [Fact]
    public async Task Migration_upgrade_and_downgrade_preserve_existing_intake_data()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await AddAsync(database);
        await using var db = await database.Factory.CreateDbContextAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261003174654_InitialTransactionStorage");
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        await migrator.MigrateAsync();
        var transaction = (await database.Store.FindAsync(id, None))!;
        Assert.Equal(TransactionStatus.Received, transaction.Current.Status);
        Assert.Single(transaction.History);
        Assert.Equal("{\"original\":true}", await database.Store.ReadRequestAsync(id, None));
        Assert.Equal(new[] { id }, await database.Store.FindDueAsync(TransactionStatus.Received, Now, 10, None));
    }

    private sealed class CancelAfterSave(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return ValueTask.FromResult(result);
        }
    }

    private static async Task<Guid> AddAsync(SqlTestDatabase database, string messageType = "pacs.008", DateTimeOffset? at = null)
    {
        var transaction = new PaymentTransaction(Guid.NewGuid(), messageType, TransactionDirection.Outgoing, at ?? Now, Guid.NewGuid().ToString("N"));
        return (await database.Store.GetOrAddOutgoingAsync(transaction, "{\"original\":true}", None)).Transaction.Id;
    }

    private static OutgoingTransactionWork Work(SqlTestDatabase database, DateTimeOffset at) => new(database.Store, new FixedTime(at));

    private static Func<PaymentTransaction, bool> Change(TransactionStatus status, DateTimeOffset at, bool accept = true) => transaction =>
    {
        transaction.ChangeStatus(status, StatusSource.Gateway, at);
        return accept;
    };

    private sealed class FixedTime(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }
}
