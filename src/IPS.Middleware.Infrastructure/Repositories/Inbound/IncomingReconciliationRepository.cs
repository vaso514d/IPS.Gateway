using System.Linq.Expressions;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using static IPS.Middleware.Infrastructure.Persistence.Inbound.IncomingPaymentColumns;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class IncomingReconciliationRepository(TransactionDbContext db) : IIncomingReconciliationRepository
{
    public async Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        return await db.IncomingPayments.AsNoTracking().Where(DueAt(now))
            .OrderBy(p => EF.Property<DateTimeOffset?>(p, FollowUpAtUtc)).ThenBy(p => p.RegisteredAtUtc).ThenBy(p => p.Id)
            .Take(take).Select(p => p.Id).ToListAsync(cancellationToken);
    }

    public async Task<IncomingPaymentClaim?> StageClaimAsync(Guid paymentId, DateTimeOffset now, TimeSpan ownership, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ownership, TimeSpan.Zero);
        var payment = await db.IncomingPayments.Where(DueAt(now)).SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null) return null;
        var claim = new IncomingPaymentClaim(paymentId, Guid.NewGuid(), now.ToUniversalTime() + ownership);
        db.Entry(payment).SetClaim(claim.Token, claim.ExpiresAtUtc);
        db.AuthorizedIncomingPaymentWork.Add(paymentId);
        return claim;
    }

    public async Task StageReleaseAsync(IncomingPaymentClaim claim, DateTimeOffset now, DateTimeOffset? nextActionAtUtc, CancellationToken cancellationToken)
    {
        var payment = await new IncomingProcessingRepository(db).TouchAsync(claim, now, cancellationToken);
        var active = payment.FollowUp is IncomingFollowUp.ReconciliationRequired or IncomingFollowUp.ReversalRequired;
        if (payment.IpsAccepted != false || active != (nextActionAtUtc is not null))
            throw new InvalidOperationException("Only active follow-up obligations may be scheduled.");
        var entry = db.Entry(payment);
        entry.Property<DateTimeOffset?>(FollowUpAtUtc).CurrentValue = nextActionAtUtc?.ToUniversalTime();
        entry.Property<DateTimeOffset?>(NextActionAtUtc).CurrentValue = null;
        entry.SetClaim(null, null);
    }

    private static Expression<Func<IncomingPayment, bool>> DueAt(DateTimeOffset now) => p =>
        p.IpsAccepted == false && (p.FollowUp == IncomingFollowUp.ReconciliationRequired || p.FollowUp == IncomingFollowUp.ReversalRequired) &&
        EF.Property<DateTimeOffset?>(p, FollowUpAtUtc) <= now &&
        (EF.Property<Guid?>(p, ClaimToken) == null || EF.Property<DateTimeOffset?>(p, ClaimExpiresAtUtc) <= now);
}
