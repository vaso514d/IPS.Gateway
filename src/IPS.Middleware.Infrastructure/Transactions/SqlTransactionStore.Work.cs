using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Transactions;

public sealed partial class SqlTransactionStore
{
    public async Task<IReadOnlyList<Guid>> FindDueAsync(
        TransactionStatus status, DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        ValidateTake(take);
        if (status is not (TransactionStatus.Received or TransactionStatus.Uncertain))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var matching = db.Transactions.AsNoTracking().Where(row =>
            row.Direction == TransactionDirection.Outgoing && row.CurrentStatus == status &&
            row.ClaimToken == null && (row.NextActionAtUtc == null || row.NextActionAtUtc <= now));
        return await FindPrioritizedAsync(matching, take, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> FindExpiredAsync(
        DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        ValidateTake(take);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await FindPrioritizedAsync(db.Transactions.AsNoTracking().Where(row =>
            row.Direction == TransactionDirection.Outgoing && row.ClaimToken != null && row.ClaimExpiresAtUtc <= now),
            take, cancellationToken);
    }

    public async Task<TransactionClaim?> TryClaimAsync(
        Guid id, DateTimeOffset now, TimeSpan duration,
        Func<PaymentTransaction, bool> start, CancellationToken cancellationToken)
    {
        ValidateId(id);
        ArgumentNullException.ThrowIfNull(start);
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var expiresAt = now.Add(duration).ToUniversalTime();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await LoadAsync(db, entry => entry.Id == id, cancellationToken);
        if (row is null || row.Direction != TransactionDirection.Outgoing ||
            row.ClaimToken is not null || row.NextActionAtUtc > now)
        {
            return null;
        }

        var transaction = row.ToDomain();
        var previousCount = transaction.History.Count;
        if (!start(transaction) || transaction.History.Count == previousCount)
        {
            return null;
        }

        var claim = new TransactionClaim(id, Guid.NewGuid(), expiresAt);
        row.ClaimToken = claim.Token;
        row.ClaimExpiresAtUtc = expiresAt;
        row.NextActionAtUtc = null;
        return await PersistAsync(db, row, transaction, previousCount, cancellationToken) == TransactionUpdateResult.Saved
            ? claim : null;
    }

    public async Task<TransactionUpdateResult> TryCompleteAsync(
        TransactionClaim claim, DateTimeOffset now, Func<PaymentTransaction, bool> change,
        DateTimeOffset? nextActionAtUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ValidateId(claim.TransactionId);
        if (claim.Token == Guid.Empty)
        {
            throw new ArgumentException("A claim token is required.", nameof(claim));
        }
        ArgumentNullException.ThrowIfNull(change);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await LoadAsync(db, entry => entry.Id == claim.TransactionId, cancellationToken);
        if (row is null)
        {
            return TransactionUpdateResult.NotFound;
        }
        if (row.ClaimToken != claim.Token || row.ClaimExpiresAtUtc is not { } expiresAt || expiresAt <= now)
        {
            return TransactionUpdateResult.Conflict;
        }

        var transaction = row.ToDomain();
        var previousCount = transaction.History.Count;
        if (!change(transaction) || transaction.History.Count == previousCount)
        {
            return TransactionUpdateResult.Unchanged;
        }

        row.ClaimToken = null;
        row.ClaimExpiresAtUtc = null;
        row.NextActionAtUtc = nextActionAtUtc?.ToUniversalTime();
        return await PersistAsync(db, row, transaction, previousCount, cancellationToken);
    }

    public async Task<TransactionUpdateResult> TryRecoverAsync(
        Guid id, DateTimeOffset now, Func<PaymentTransaction, bool> recover, CancellationToken cancellationToken)
    {
        ValidateId(id);
        ArgumentNullException.ThrowIfNull(recover);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await LoadAsync(db, entry => entry.Id == id, cancellationToken);
        if (row is null)
        {
            return TransactionUpdateResult.NotFound;
        }
        if (row.ClaimToken is null || row.ClaimExpiresAtUtc is not { } expiresAt || expiresAt > now)
        {
            return TransactionUpdateResult.Unchanged;
        }

        var transaction = row.ToDomain();
        var previousCount = transaction.History.Count;
        if (!recover(transaction) || transaction.History.Count == previousCount)
        {
            return TransactionUpdateResult.Unchanged;
        }

        row.ClaimToken = null;
        row.ClaimExpiresAtUtc = null;
        row.NextActionAtUtc = now.ToUniversalTime();
        return await PersistAsync(db, row, transaction, previousCount, cancellationToken);
    }

    private static async Task<IReadOnlyList<Guid>> FindPrioritizedAsync(
        IQueryable<TransactionRow> matching, int take, CancellationToken cancellationToken)
    {
        var ids = await matching.Where(row => row.MessageType == "pacs.008")
            .OrderBy(row => row.CurrentStatusAtUtc).ThenBy(row => row.Id)
            .Select(row => row.Id).Take(take).ToListAsync(cancellationToken);
        if (ids.Count < take)
        {
            ids.AddRange(await matching.Where(row => row.MessageType != "pacs.008")
                .OrderBy(row => row.CurrentStatusAtUtc).ThenBy(row => row.Id)
                .Select(row => row.Id).Take(take - ids.Count).ToListAsync(cancellationToken));
        }
        return ids;
    }

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A transaction identity is required.", nameof(id));
        }
    }

    private static void ValidateTake(int take)
    {
        if (take is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(take), "Batch size must be between 1 and 1000.");
        }
    }
}
