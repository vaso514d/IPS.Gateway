using System.Linq.Expressions;
using IPS.Middleware.Application.Abstractions.Inbound;
using IPS.Middleware.Application.Inbound;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class IncomingPaymentWorkRepository(TransactionDbContext db) : IIncomingPaymentWorkRepository
{
    public async Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        return await db.IncomingPayments.AsNoTracking().Where(DueAt(now))
            .OrderBy(p => EF.Property<DateTimeOffset?>(p, NextActionAtUtc)).ThenBy(p => p.RegisteredAtUtc).ThenBy(p => p.Id)
            .Take(take).Select(p => p.Id).ToListAsync(cancellationToken);
    }

    public async Task<IncomingPaymentClaim?> StageClaimAsync(Guid paymentId, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        var payment = await db.IncomingPayments.Where(DueAt(now)).SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null) return null;
        var claim = new IncomingPaymentClaim(payment.Id, Guid.NewGuid(), now.ToUniversalTime() + duration);
        Own(payment, claim.Token, claim.ExpiresAtUtc);
        return claim;
    }

    public async Task<bool> StageReleaseAsync(IncomingPaymentClaim claim, DateTimeOffset now, DateTimeOffset nextActionAtUtc,
        CancellationToken cancellationToken)
    {
        db.RequireUsable();
        if (await db.IncomingPayments.FindAsync([claim.PaymentId], cancellationToken) is not { } payment) return false;
        var entry = db.Entry(payment);
        if (entry.Property<Guid?>(ClaimToken).CurrentValue != claim.Token ||
            entry.Property<DateTimeOffset?>(ClaimExpiresAtUtc).CurrentValue is not { } expiry || expiry <= now)
            return false;
        entry.Property<DateTimeOffset?>(NextActionAtUtc).CurrentValue = nextActionAtUtc.ToUniversalTime();
        Own(payment, null, null);
        return true;
    }

    // Discovery and acquisition share one definition: due and without a live owner.
    private static Expression<Func<IncomingPayment, bool>> DueAt(DateTimeOffset now) => p =>
        EF.Property<DateTimeOffset?>(p, NextActionAtUtc) <= now &&
        (EF.Property<Guid?>(p, ClaimToken) == null || EF.Property<DateTimeOffset?>(p, ClaimExpiresAtUtc) <= now);

    private void Own(IncomingPayment payment, Guid? token, DateTimeOffset? expiresAtUtc)
    {
        var entry = db.Entry(payment);
        entry.Property<Guid?>(ClaimToken).CurrentValue = token;
        entry.Property<DateTimeOffset?>(ClaimExpiresAtUtc).CurrentValue = expiresAtUtc;
        db.AuthorizedIncomingPaymentWork.Add(payment.Id);
    }
}
