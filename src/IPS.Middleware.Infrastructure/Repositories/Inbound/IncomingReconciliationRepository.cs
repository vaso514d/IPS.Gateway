using System.Linq.Expressions;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class IncomingReconciliationRepository(TransactionDbContext db) : IIncomingReconciliationRepository
{
    public async Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        return await db.IncomingMetadata.AsNoTracking().Where(DueAt(now))
            .OrderBy(p => p.FollowUpAtUtc).ThenBy(p => p.Payment.RegisteredAtUtc).ThenBy(p => p.Id)
            .Take(take).Select(p => p.Id).ToListAsync(cancellationToken);
    }

    public async Task<IncomingPaymentClaim?> StageClaimAsync(Guid paymentId, DateTimeOffset now, TimeSpan ownership, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ownership, TimeSpan.Zero);
        var payment = await db.IncomingMetadata.Include(p => p.Payment).Where(DueAt(now)).SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            return null;
        }

        var claim = new IncomingPaymentClaim(paymentId, Guid.NewGuid(), now.ToUniversalTime() + ownership);
        payment.SetClaim(claim.Token, claim.ExpiresAtUtc);
        return claim;
    }

    public async Task StageReleaseAsync(
        IncomingPaymentClaim claim,
        DateTimeOffset now,
        DateTimeOffset? nextActionAtUtc,
        CancellationToken cancellationToken)
    {
        var payment = await new IncomingProcessingRepository(db).TouchAsync(claim, now, cancellationToken);
        var active = payment.FollowUp is IncomingFollowUp.ReconciliationRequired or IncomingFollowUp.ReversalRequired;
        if (payment.IpsAccepted != false || active != (nextActionAtUtc is not null))
        {
            throw new InvalidOperationException("Only active follow-up obligations may be scheduled.");
        }

        var entry = db.Entry(db.Metadata(payment));
        entry.Entity.FollowUpAtUtc = nextActionAtUtc?.ToUniversalTime();
        entry.Entity.NextActionAtUtc = null;
        entry.Entity.SetClaim(null, null);
    }

    private static Expression<Func<IncomingPaymentMetadata, bool>> DueAt(DateTimeOffset now) => p =>
        p.Payment.IpsAccepted == false && (p.Payment.FollowUp == IncomingFollowUp.ReconciliationRequired || p.Payment.FollowUp == IncomingFollowUp.ReversalRequired) &&
        p.FollowUpAtUtc <= now &&
        (p.ClaimToken == null || p.ClaimExpiresAtUtc <= now);
}
