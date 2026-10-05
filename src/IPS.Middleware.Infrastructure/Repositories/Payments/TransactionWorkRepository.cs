using System.Linq.Expressions;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Repositories.Payments;

public sealed class TransactionWorkRepository(TransactionDbContext db) : ITransactionWorkRepository
{
    public async Task<IReadOnlyList<Guid>> FindDueAsync(
        TransactionStatus status, DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        ValidateTake(take);
        // Unowned Sending is preparation released for retry; recovery turns abandoned submissions into Uncertain.
        if (status is not (TransactionStatus.Received or TransactionStatus.Sending or TransactionStatus.Uncertain))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return await PrioritizeAsync(p => p.Payment.CurrentStatus == status && p.ClaimToken == null &&
            (p.NextActionAtUtc == null || p.NextActionAtUtc <= now),
            take, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> FindExpiredAsync(DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        ValidateTake(take);
        return await PrioritizeAsync(p => p.ClaimToken != null &&
            p.ClaimExpiresAtUtc <= now, take, cancellationToken);
    }

    public TransactionClaim? StageClaim(OutgoingPayment payment, DateTimeOffset now, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var entry = Tracked(payment);
        if (entry.Entity.ClaimToken is not null || entry.Entity.NextActionAtUtc > now)
        {
            return null;
        }

        var claim = new TransactionClaim(payment.Id, Guid.NewGuid(), now.Add(duration).ToUniversalTime());
        Own(entry, claim.Token, claim.ExpiresAtUtc, nextAction: null);
        return claim;
    }

    public bool StageCompletion(OutgoingPayment payment, TransactionClaim claim, DateTimeOffset now, DateTimeOffset? nextActionAtUtc)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (claim.Token == Guid.Empty)
        {
            throw new ArgumentException("A claim token is required.", nameof(claim));
        }

        var entry = Tracked(payment);
        if (!entry.Entity.HasLiveClaim(claim, now))
        {
            return false;
        }

        Own(entry, null, null, nextActionAtUtc?.ToUniversalTime());
        return true;
    }

    public bool StageRecovery(OutgoingPayment payment, DateTimeOffset now)
    {
        var entry = Tracked(payment);
        if (entry.Entity.ClaimToken is null || entry.Entity.ClaimExpiresAtUtc is not { } expiry || expiry > now)
        {
            return false;
        }

        Own(entry, null, null, now.ToUniversalTime());
        return true;
    }

    private void Own(EntityEntry<OutgoingPaymentMetadata> entry, Guid? token, DateTimeOffset? expiry, DateTimeOffset? nextAction)
    {
        entry.Entity.ClaimToken = token;
        entry.Entity.ClaimExpiresAtUtc = expiry;
        entry.Entity.NextActionAtUtc = nextAction;
        db.Changes.AuthorizedOwnership.Add(entry.Entity.Id);
    }

    private EntityEntry<OutgoingPaymentMetadata> Tracked(OutgoingPayment payment)
    {
        db.RequireUsable();
        var entry = db.Entry(db.Metadata(payment));
        if (entry.State is EntityState.Detached or EntityState.Added)
        {
            throw new InvalidOperationException("Ownership requires a persisted payment loaded in this scope.");
        }

        return entry;
    }

    // pacs.008 first, then oldest status change; Id breaks ties deterministically.
    private async Task<IReadOnlyList<Guid>> PrioritizeAsync(
        Expression<Func<OutgoingPaymentMetadata, bool>> matching, int take, CancellationToken cancellationToken) =>
        await db.OutgoingMetadata.AsNoTracking().Where(matching)
            .OrderBy(p => p.Payment.MessageType == Pacs008 ? 0 : 1).ThenBy(p => p.Payment.CurrentStatusAtUtc).ThenBy(p => p.Id)
            .Select(p => p.Id).Take(take).ToListAsync(cancellationToken);

    private static void ValidateTake(int take)
    {
        if (take is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }
    }
}
