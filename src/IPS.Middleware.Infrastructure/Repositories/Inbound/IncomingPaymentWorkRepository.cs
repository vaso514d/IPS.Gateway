using System.Linq.Expressions;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
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
        return await db.IncomingMetadata.AsNoTracking().Where(DueAt(now))
            .OrderBy(p => p.NextActionAtUtc).ThenBy(p => p.Payment.RegisteredAtUtc).ThenBy(p => p.Id)
            .Take(take).Select(p => p.Id).ToListAsync(cancellationToken);
    }

    public async Task<IncomingPaymentClaim?> StageClaimAsync(Guid paymentId, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        var payment = await db.IncomingMetadata.Include(p => p.Payment).Where(DueAt(now)).SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            return null;
        }

        var claim = new IncomingPaymentClaim(payment.Id, Guid.NewGuid(), now.ToUniversalTime() + duration);
        Own(payment, claim.Token, claim.ExpiresAtUtc);
        return claim;
    }

    public async Task<bool> StageReleaseAsync(
        IncomingPaymentClaim claim,
        DateTimeOffset now,
        DateTimeOffset nextActionAtUtc,
        CancellationToken cancellationToken)
    {
        db.RequireUsable();
        if (await db.IncomingMetadata.FindAsync([claim.PaymentId], cancellationToken) is not { } payment)
        {
            return false;
        }

        var entry = db.Entry(payment);
        if (!entry.Entity.HasLiveClaim(claim, now))
        {
            return false;
        }

        entry.Entity.NextActionAtUtc = nextActionAtUtc.ToUniversalTime();
        Own(payment, null, null);
        return true;
    }

    // Discovery and acquisition share one definition: due and without a live owner.
    private static Expression<Func<IncomingPaymentMetadata, bool>> DueAt(DateTimeOffset now) => p =>
        p.NextActionAtUtc <= now &&
        (p.ClaimToken == null || p.ClaimExpiresAtUtc <= now);

    private void Own(IncomingPaymentMetadata payment, Guid? token, DateTimeOffset? expiresAtUtc)
    {
        payment.SetClaim(token, expiresAtUtc);
        db.Changes.AuthorizedIncomingPaymentWork.Add(payment.Id);
    }
}
