using System.Data;
using System.Linq.Expressions;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Transactions;

public sealed class SqlTransactionStore(IDbContextFactory<TransactionDbContext> contextFactory) : ITransactionStore
{
    public async Task<TransactionIntakeResult> GetOrAddOutgoingAsync(
        PaymentTransaction transaction, string requestJson, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestJson);
        if (transaction.Direction != TransactionDirection.Outgoing || transaction.ClientReference is null ||
            transaction.CoreReference is not null || transaction.History.Count != 1)
        {
            throw new ArgumentException("Outgoing intake requires a new transaction with a client reference.", nameof(transaction));
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await LoadAsync(db, row => row.ClientReference == transaction.ClientReference, cancellationToken);
        if (existing is not null)
        {
            return new(existing.ToDomain(), Created: false);
        }

        var row = new TransactionRow
        {
            Id = transaction.Id,
            MessageType = transaction.MessageType,
            Direction = transaction.Direction,
            ClientReference = transaction.ClientReference,
            CreatedAtUtc = transaction.CreatedAtUtc,
            RequestJson = requestJson,
            History = transaction.History.Select(entry => TransactionHistoryRow.From(transaction.Id, entry)).ToList()
        };
        row.SetCurrent(transaction);
        db.Transactions.Add(row);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new(row.ToDomain(), Created: true);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A duplicate key is intake idempotency only when the requested client reference actually exists.
            db.ChangeTracker.Clear();
            var winner = await LoadAsync(db, entry => entry.ClientReference == transaction.ClientReference, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return new(winner.ToDomain(), Created: false);
        }
    }

    public async Task<PaymentTransaction?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return (await LoadAsync(db, row => row.Id == id, cancellationToken))?.ToDomain();
    }

    public async Task<string?> ReadRequestAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Transactions.Where(row => row.Id == id)
            .Select(row => row.RequestJson).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<TransactionUpdateResult> TryUpdateAsync(
        Guid id, Func<PaymentTransaction, bool> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await LoadAsync(db, entry => entry.Id == id, cancellationToken);
        if (row is null)
        {
            return TransactionUpdateResult.NotFound;
        }

        var transaction = row.ToDomain();
        var previousCount = transaction.History.Count;
        if (!change(transaction) || transaction.History.Count == previousCount)
        {
            return TransactionUpdateResult.Unchanged;
        }

        await using var write = await db.Database.BeginTransactionAsync(cancellationToken);
        row.SetCurrent(transaction);
        try
        {
            // Acquire the row-version-checked write before inserting history, so a losing writer cannot collide on sequence.
            await db.SaveChangesAsync(cancellationToken);
            db.History.AddRange(transaction.History.Skip(previousCount)
                .Select(entry => TransactionHistoryRow.From(id, entry)));
            await db.SaveChangesAsync(cancellationToken);
            await write.CommitAsync(cancellationToken);
            return TransactionUpdateResult.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionUpdateResult.Conflict;
        }
    }

    private static async Task<TransactionRow?> LoadAsync(
        TransactionDbContext db, Expression<Func<TransactionRow, bool>> predicate, CancellationToken cancellationToken)
    {
        // Keep the parent and all history coherent during the read; release read locks before running a caller's decision.
        await using var read = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var row = await db.Transactions.Include(entry => entry.History).AsSingleQuery()
            .SingleOrDefaultAsync(predicate, cancellationToken);
        await read.CommitAsync(cancellationToken);
        return row;
    }
}
