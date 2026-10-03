using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Transactions;

public sealed class SqlTransactionStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migration_applies_rolls_back_and_matches_the_model()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var db = await database.Factory.CreateDbContextAsync();
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("0");
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        await migrator.MigrateAsync();
        var accepted = await database.Store.GetOrAddOutgoingAsync(New(), "{}", CancellationToken.None);
        Assert.True(accepted.Created);
    }

    [Fact]
    public async Task Intake_survives_new_store_and_context_instances_with_exact_request()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        const string payload = " {\"amount\":10.50,\"name\":\"გადახდა\"} ";
        var accepted = await new OutgoingTransactionIntake(database.Store, TimeProvider.System)
            .AcceptAsync("pacs.008", " CBS-1 ", payload, CancellationToken.None);

        var reloaded = await database.Store.FindAsync(accepted.Transaction.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.NotSame(accepted.Transaction, reloaded);
        Assert.Equal("CBS-1", reloaded.ClientReference);
        Assert.Equal(TransactionStatus.Received, reloaded.Current.Status);
        Assert.Single(reloaded.History);
        Assert.Equal(payload, await database.Store.ReadRequestAsync(reloaded.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Reusing_reference_across_message_types_returns_original_outcome_and_payload()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var first = await database.Store.GetOrAddOutgoingAsync(New(), "{\"amount\":10}", CancellationToken.None);
        await database.Store.TryUpdateAsync(first.Transaction.Id, transaction =>
        {
            transaction.ChangeStatus(TransactionStatus.Accepted, StatusSource.Ips, Now.AddSeconds(1), description: "Accepted");
            return true;
        }, CancellationToken.None);

        var repeat = await database.Store.GetOrAddOutgoingAsync(New("pacs.009"), "{\"amount\":99}", CancellationToken.None);

        Assert.False(repeat.Created);
        Assert.Equal(first.Transaction.Id, repeat.Transaction.Id);
        Assert.Equal("pacs.008", repeat.Transaction.MessageType);
        Assert.Equal(TransactionStatus.Accepted, repeat.Transaction.Current.Status);
        Assert.Equal("Accepted", repeat.Transaction.Current.Description);
        Assert.Equal("{\"amount\":10}", await database.Store.ReadRequestAsync(first.Transaction.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_intake_has_one_winner_and_no_orphan_requests_or_history()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var candidates = Enumerable.Range(0, 8).Select(_ => New()).ToArray();
        var results = await Task.WhenAll(candidates.Select(transaction =>
            database.Store.GetOrAddOutgoingAsync(transaction, transaction.Id.ToString(), CancellationToken.None)));

        var winner = Assert.Single(results.Where(result => result.Created));
        Assert.All(results, result => Assert.Equal(winner.Transaction.Id, result.Transaction.Id));
        Assert.Single(winner.Transaction.History);
        Assert.Equal(winner.Transaction.Id.ToString(), await database.Store.ReadRequestAsync(winner.Transaction.Id, CancellationToken.None));
        foreach (var loser in candidates.Where(transaction => transaction.Id != winner.Transaction.Id))
        {
            Assert.Null(await database.Store.FindAsync(loser.Id, CancellationToken.None));
            Assert.Null(await database.Store.ReadRequestAsync(loser.Id, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Unrelated_duplicate_key_is_not_mistaken_for_idempotency()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var first = New();
        await database.Store.GetOrAddOutgoingAsync(first, "{}", CancellationToken.None);
        var conflictingId = new PaymentTransaction(first.Id, "pacs.008", TransactionDirection.Outgoing, Now, "OTHER");

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            database.Store.GetOrAddOutgoingAsync(conflictingId, "{}", CancellationToken.None));
        var original = await database.Store.FindAsync(first.Id, CancellationToken.None);
        Assert.Equal("CBS-1", original!.ClientReference);
        Assert.Single(original.History);
    }

    [Fact]
    public async Task Failed_initial_history_insert_rolls_back_transaction_and_request()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var transaction = New();
        await using var db = await database.Factory.CreateDbContextAsync();
        // A database constraint injects a real SQL failure after the parent insert.
        var sql = $"ALTER TABLE [TransactionHistory] ADD CONSTRAINT [CK_Test_IntakeFailure] CHECK ([TransactionId] <> '{transaction.Id:D}')";
        await db.Database.ExecuteSqlRawAsync(sql);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            database.Store.GetOrAddOutgoingAsync(transaction, "{}", CancellationToken.None));

        Assert.Null(await database.Store.FindAsync(transaction.Id, CancellationToken.None));
        Assert.Null(await database.Store.ReadRequestAsync(transaction.Id, CancellationToken.None));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [TransactionHistory] DROP CONSTRAINT [CK_Test_IntakeFailure]");
        Assert.True((await database.Store.GetOrAddOutgoingAsync(New(), "{}", CancellationToken.None)).Created);
    }

    [Fact]
    public async Task Status_and_steps_round_trip_in_append_order_and_keep_current_details()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var transaction = New();
        await database.Store.GetOrAddOutgoingAsync(transaction, "{}", CancellationToken.None);

        var result = await database.Store.TryUpdateAsync(transaction.Id, current =>
        {
            current.ChangeStatus(TransactionStatus.Sending, StatusSource.Gateway, Now);
            current.RecordStep(ProcessingStep.Sent, Now.AddSeconds(-1));
            current.ChangeStatus(TransactionStatus.Rejected, StatusSource.Ips, Now, " ac01 ", 1009, " Invalid IBAN ");
            current.RecordStep(ProcessingStep.IpsResponded, Now);
            return true;
        }, CancellationToken.None);

        Assert.Equal(TransactionUpdateResult.Saved, result);
        var reloaded = await database.Store.FindAsync(transaction.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, reloaded.History.Select(entry => entry.Sequence));
        Assert.Equal(4, reloaded.Current.Sequence);
        Assert.Equal("AC01", reloaded.Current.ReasonCode);
        Assert.Equal(1009, reloaded.Current.IpsInternalCode);
        Assert.Equal("Invalid IBAN", reloaded.Current.Description);
        Assert.Equal(Now.AddSeconds(-1), reloaded.History[2].AtUtc);
        Assert.Equal(ProcessingStep.IpsResponded, reloaded.History[4].Step);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Competing_updates_commit_only_one_current_state_and_history(bool stepsOnly)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var transaction = New();
        await database.Store.GetOrAddOutgoingAsync(transaction, "{}", CancellationToken.None);
        using var bothRead = new Barrier(2);

        Task<TransactionUpdateResult> Update(TransactionStatus status) => Task.Run(() =>
            database.Store.TryUpdateAsync(transaction.Id, current =>
            {
                if (!bothRead.SignalAndWait(TimeSpan.FromSeconds(15)))
                {
                    throw new TimeoutException("Both writers did not reach the same initial version.");
                }

                if (stepsOnly)
                {
                    current.RecordStep(ProcessingStep.Sent, Now);
                }
                else
                {
                    current.ChangeStatus(status, StatusSource.Ips, Now);
                }
                return true;
            }, CancellationToken.None));

        var results = await Task.WhenAll(Update(TransactionStatus.Accepted), Update(TransactionStatus.Rejected));

        Assert.Single(results.Where(result => result == TransactionUpdateResult.Saved));
        Assert.Single(results.Where(result => result == TransactionUpdateResult.Conflict));
        var reloaded = await database.Store.FindAsync(transaction.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded.History.Count);
        if (stepsOnly)
        {
            Assert.Equal(TransactionStatus.Received, reloaded.Current.Status);
            Assert.Equal(1, reloaded.Current.Sequence);
        }
        else
        {
            Assert.Contains(reloaded.Current.Status, new[] { TransactionStatus.Accepted, TransactionStatus.Rejected });
        }
    }

    [Fact]
    public async Task Missing_initial_history_is_not_silently_recreated()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var transaction = New();
        await database.Store.GetOrAddOutgoingAsync(transaction, "{}", CancellationToken.None);
        await database.Store.TryUpdateAsync(transaction.Id, current =>
        {
            current.ChangeStatus(TransactionStatus.Sending, StatusSource.Gateway, Now);
            return true;
        }, CancellationToken.None);
        await using var db = await database.Factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [TransactionHistory] WHERE [TransactionId] = {transaction.Id} AND [Sequence] = 1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Store.FindAsync(transaction.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Failed_history_append_rolls_back_current_status_update()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var transaction = New();
        await database.Store.GetOrAddOutgoingAsync(transaction, "{}", CancellationToken.None);

        await Assert.ThrowsAsync<DbUpdateException>(() => database.Store.TryUpdateAsync(transaction.Id, current =>
        {
            current.ChangeStatus(TransactionStatus.Rejected, StatusSource.Ips, Now, new string('x', 36));
            return true;
        }, CancellationToken.None));

        var reloaded = await database.Store.FindAsync(transaction.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal(TransactionStatus.Received, reloaded.Current.Status);
        Assert.Single(reloaded.History);
    }

    [Fact]
    public async Task Declined_decision_and_thrown_decision_leave_storage_unchanged()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var transaction = New();
        await database.Store.GetOrAddOutgoingAsync(transaction, "{}", CancellationToken.None);
        var declined = await database.Store.TryUpdateAsync(transaction.Id, current =>
        {
            current.ChangeStatus(TransactionStatus.Rejected, StatusSource.Core, Now);
            return false;
        }, CancellationToken.None);

        Assert.Equal(TransactionUpdateResult.Unchanged, declined);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Store.TryUpdateAsync(transaction.Id, current =>
        {
            current.ChangeStatus(TransactionStatus.Accepted, StatusSource.Core, Now);
            throw new InvalidOperationException("Decision failed");
        }, CancellationToken.None));
        Assert.Single((await database.Store.FindAsync(transaction.Id, CancellationToken.None))!.History);
        Assert.Equal(TransactionUpdateResult.Unchanged,
            await database.Store.TryUpdateAsync(transaction.Id, _ => true, CancellationToken.None));
        Assert.Equal(TransactionUpdateResult.NotFound,
            await database.Store.TryUpdateAsync(Guid.NewGuid(), _ => throw new InvalidOperationException(), CancellationToken.None));
    }

    [Fact]
    public async Task Cancelled_write_is_not_persisted()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var transaction = New();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.Store.GetOrAddOutgoingAsync(transaction, "{}", cancellation.Token));
        Assert.Null(await database.Store.FindAsync(transaction.Id, CancellationToken.None));
    }

    private static PaymentTransaction New(string messageType = "pacs.008") =>
        new(Guid.NewGuid(), messageType, TransactionDirection.Outgoing, Now, "CBS-1");
}
