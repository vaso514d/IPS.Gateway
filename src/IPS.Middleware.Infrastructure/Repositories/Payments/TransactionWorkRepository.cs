using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace IPS.Middleware.Infrastructure.Repositories.Payments;

public sealed class TransactionWorkRepository(TransactionDbContext db) : ITransactionWorkRepository
{
    public async Task<IReadOnlyList<Guid>> FindDueAsync(
        TransactionStatus status, DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        ValidateTake(take);
        if (status is not (TransactionStatus.Received or TransactionStatus.Uncertain))
            throw new ArgumentOutOfRangeException(nameof(status));
        return await PrioritizeAsync(db.Payments.AsNoTracking().Where(p => p.CurrentStatus == status &&
            EF.Property<Guid?>(p, "ClaimToken") == null &&
            (EF.Property<DateTimeOffset?>(p, "NextActionAtUtc") == null || EF.Property<DateTimeOffset?>(p, "NextActionAtUtc") <= now)),
            take, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> FindExpiredAsync(DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        ValidateTake(take);
        return await PrioritizeAsync(db.Payments.AsNoTracking().Where(p => EF.Property<Guid?>(p, "ClaimToken") != null &&
            EF.Property<DateTimeOffset?>(p, "ClaimExpiresAtUtc") <= now), take, cancellationToken);
    }

    public TransactionClaim? StageClaim(OutgoingPayment payment, DateTimeOffset now, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        var entry = Tracked(payment);
        if (entry.Property<Guid?>("ClaimToken").CurrentValue is not null ||
            entry.Property<DateTimeOffset?>("NextActionAtUtc").CurrentValue > now) return null;
        var claim = new TransactionClaim(payment.Id, Guid.NewGuid(), now.Add(duration).ToUniversalTime());
        entry.Property<Guid?>("ClaimToken").CurrentValue = claim.Token;
        entry.Property<DateTimeOffset?>("ClaimExpiresAtUtc").CurrentValue = claim.ExpiresAtUtc;
        entry.Property<DateTimeOffset?>("NextActionAtUtc").CurrentValue = null;
        db.AuthorizedOwnership.Add(payment.Id);
        return claim;
    }

    public bool StageCompletion(OutgoingPayment payment, TransactionClaim claim, DateTimeOffset now, DateTimeOffset? nextActionAtUtc)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (claim.Token == Guid.Empty) throw new ArgumentException("A claim token is required.", nameof(claim));
        var entry = Tracked(payment);
        if (!PaymentOwnership.HasLiveClaim(entry, claim, now)) return false;
        Release(entry, nextActionAtUtc);
        return true;
    }

    public bool StageRecovery(OutgoingPayment payment, DateTimeOffset now)
    {
        var entry = Tracked(payment);
        if (entry.Property<Guid?>("ClaimToken").CurrentValue is null ||
            entry.Property<DateTimeOffset?>("ClaimExpiresAtUtc").CurrentValue is not { } expiry || expiry > now) return false;
        Release(entry, now);
        return true;
    }

    private void Release(EntityEntry<OutgoingPayment> entry, DateTimeOffset? nextAction)
    {
        entry.Property<Guid?>("ClaimToken").CurrentValue = null;
        entry.Property<DateTimeOffset?>("ClaimExpiresAtUtc").CurrentValue = null;
        entry.Property<DateTimeOffset?>("NextActionAtUtc").CurrentValue = nextAction?.ToUniversalTime();
        db.AuthorizedOwnership.Add(entry.Entity.Id);
    }

    private EntityEntry<OutgoingPayment> Tracked(OutgoingPayment payment)
    {
        db.RequireUsable();
        var entry = db.Entry(payment);
        if (entry.State is EntityState.Detached or EntityState.Added)
            throw new InvalidOperationException("Ownership requires a persisted payment loaded in this scope.");
        return entry;
    }

    private static async Task<IReadOnlyList<Guid>> PrioritizeAsync(
        IQueryable<OutgoingPayment> matching, int take, CancellationToken cancellationToken)
    {
        var ids = await matching.Where(p => p.MessageType == "pacs.008")
            .OrderBy(p => p.CurrentStatusAtUtc).ThenBy(p => p.Id).Select(p => p.Id).Take(take).ToListAsync(cancellationToken);
        if (ids.Count < take)
            ids.AddRange(await matching.Where(p => p.MessageType != "pacs.008").OrderBy(p => p.CurrentStatusAtUtc)
                .ThenBy(p => p.Id).Select(p => p.Id).Take(take - ids.Count).ToListAsync(cancellationToken));
        return ids;
    }

    private static void ValidateTake(int take)
    {
        if (take is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(take));
    }
}
