using System.Linq.Expressions;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class IncomingReconciliationRepository(TransactionDbContext db) : IIncomingReconciliationRepository
{
    public async Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) =>
        await db.IncomingMetadata
            .AsNoTracking()
            .Where(DueAt(now))
            .OrderBy(x => x.FollowUpAtUtc)
            .ThenBy(x => x.Payment.RegisteredAtUtc)
            .ThenBy(x => x.Id)
            .Take(take)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<IncomingPaymentClaim?> StageClaimAsync(Guid paymentId, DateTimeOffset now, TimeSpan ownership, CancellationToken cancellationToken)
    {
        var metadata = await db.IncomingMetadata
            .Include(x => x.Payment)
            .Where(DueAt(now))
            .SingleOrDefaultAsync(x => x.Id == paymentId, cancellationToken);
        if (metadata is null)
        {
            return null;
        }

        var claim = new IncomingPaymentClaim(paymentId, Guid.NewGuid(), now.ToUniversalTime() + ownership);
        metadata.SetClaim(claim.Token, claim.ExpiresAtUtc);
        return claim;
    }

    public async Task StageReleaseAsync(IncomingPaymentClaim claim, DateTimeOffset now, DateTimeOffset? nextActionAtUtc, CancellationToken cancellationToken)
    {
        var payment = await db.OwnedIncomingPaymentAsync(claim, now, cancellationToken);
        var followUpActive = payment.FollowUp is IncomingFollowUp.ReconciliationRequired or IncomingFollowUp.ReversalRequired;
        if (payment.IpsAccepted != false || followUpActive != nextActionAtUtc.HasValue)
        {
            throw new InvalidOperationException("Only active follow-up obligations may be scheduled.");
        }

        var metadata = db.Metadata(payment);
        metadata.FollowUpAtUtc = nextActionAtUtc?.ToUniversalTime();
        metadata.NextActionAtUtc = null;
        metadata.SetClaim(null, null);
    }

    private static Expression<Func<IncomingPaymentMetadata, bool>> DueAt(DateTimeOffset now) =>
        x => x.Payment.IpsAccepted == false
            && (x.Payment.FollowUp == IncomingFollowUp.ReconciliationRequired || x.Payment.FollowUp == IncomingFollowUp.ReversalRequired)
            && x.FollowUpAtUtc <= now
            && (x.ClaimToken == null || x.ClaimExpiresAtUtc <= now);
}
