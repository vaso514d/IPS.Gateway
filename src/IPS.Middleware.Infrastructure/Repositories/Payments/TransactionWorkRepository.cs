using System.Linq.Expressions;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Repositories.Payments;

public sealed class TransactionWorkRepository(TransactionDbContext db) : ITransactionWorkRepository
{
    public Task<IReadOnlyList<Guid>> FindDueAsync(TransactionStatus status, DateTimeOffset now, int take, CancellationToken cancellationToken) =>
        FindPrioritizedAsync(IsDue(status, now), take, cancellationToken);

    public Task<IReadOnlyList<Guid>> FindExpiredAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) =>
        FindPrioritizedAsync(x => x.ClaimToken != null && x.ClaimExpiresAtUtc <= now, take, cancellationToken);

    public TransactionClaim? StageClaim(OutgoingPayment payment, DateTimeOffset now, TimeSpan duration)
    {
        var metadata = PersistedMetadata(payment);
        if (metadata.ClaimToken is not null || metadata.NextActionAtUtc > now)
        {
            return null;
        }

        var claim = new TransactionClaim(payment.Id, Guid.NewGuid(), now.Add(duration).ToUniversalTime());
        SetOwnership(metadata, claim.Token, claim.ExpiresAtUtc, nextActionAtUtc: null);
        return claim;
    }

    public bool StageCompletion(OutgoingPayment payment, TransactionClaim claim, DateTimeOffset now, DateTimeOffset? nextActionAtUtc)
    {
        var metadata = PersistedMetadata(payment);
        if (!metadata.HasLiveClaim(claim, now))
        {
            return false;
        }

        SetOwnership(metadata, token: null, expiresAtUtc: null, nextActionAtUtc?.ToUniversalTime());
        return true;
    }

    public bool StageRecovery(OutgoingPayment payment, DateTimeOffset now)
    {
        var metadata = PersistedMetadata(payment);
        var expired = metadata.ClaimToken is not null && metadata.ClaimExpiresAtUtc <= now;
        if (!expired)
        {
            return false;
        }

        SetOwnership(metadata, token: null, expiresAtUtc: null, now.ToUniversalTime());
        return true;
    }

    // Unowned Sending is preparation released for retry; recovery turns abandoned submissions into Uncertain.
    private static Expression<Func<OutgoingPaymentMetadata, bool>> IsDue(TransactionStatus status, DateTimeOffset now) =>
        x => x.Payment.CurrentStatus == status
            && x.ClaimToken == null
            && (x.NextActionAtUtc == null || x.NextActionAtUtc <= now);

    // pacs.008 first, then oldest status change; Id breaks ties deterministically.
    private async Task<IReadOnlyList<Guid>> FindPrioritizedAsync(
        Expression<Func<OutgoingPaymentMetadata, bool>> matching,
        int take,
        CancellationToken cancellationToken) =>
        await db.OutgoingMetadata
            .AsNoTracking()
            .Where(matching)
            .OrderBy(x => x.Payment.MessageType == Pacs008 ? 0 : 1)
            .ThenBy(x => x.Payment.CurrentStatusAtUtc)
            .ThenBy(x => x.Id)
            .Select(x => x.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    private OutgoingPaymentMetadata PersistedMetadata(OutgoingPayment payment)
    {
        var metadata = db.Metadata(payment);
        if (db.Entry(metadata).State == EntityState.Added)
        {
            throw new InvalidOperationException("Ownership requires a persisted payment loaded in this scope.");
        }

        return metadata;
    }

    private static void SetOwnership(OutgoingPaymentMetadata metadata, Guid? token, DateTimeOffset? expiresAtUtc, DateTimeOffset? nextActionAtUtc)
    {
        metadata.ClaimToken = token;
        metadata.ClaimExpiresAtUtc = expiresAtUtc;
        metadata.NextActionAtUtc = nextActionAtUtc;
    }
}
