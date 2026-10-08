using System.Linq.Expressions;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class IncomingPaymentWorkRepository(TransactionDbContext db) : IIncomingPaymentWorkRepository
{
    public async Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) =>
        await db.IncomingMetadata
            .AsNoTracking()
            .Where(DueAt(now))
            .OrderBy(x => x.NextActionAtUtc)
            .ThenBy(x => x.Payment.RegisteredAtUtc)
            .ThenBy(x => x.Id)
            .Take(take)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<IncomingPaymentClaim?> StageClaimAsync(Guid paymentId, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken)
    {
        var metadata = await db.IncomingMetadata
            .Include(x => x.Payment)
            .Where(DueAt(now))
            .SingleOrDefaultAsync(x => x.Id == paymentId, cancellationToken);
        if (metadata is null)
        {
            return null;
        }

        var claim = new IncomingPaymentClaim(metadata.Id, Guid.NewGuid(), now.ToUniversalTime() + duration);
        metadata.SetClaim(claim.Token, claim.ExpiresAtUtc);
        return claim;
    }

    public async Task<bool> StageReleaseAsync(IncomingPaymentClaim claim, DateTimeOffset now, DateTimeOffset nextActionAtUtc, CancellationToken cancellationToken)
    {
        var metadata = await db.IncomingMetadata.FindAsync([claim.PaymentId], cancellationToken);
        if (metadata is null || !metadata.HasLiveClaim(claim, now))
        {
            return false;
        }

        metadata.NextActionAtUtc = nextActionAtUtc.ToUniversalTime();
        metadata.SetClaim(null, null);
        return true;
    }

    // Discovery and acquisition share one definition: due and without a live owner.
    private static Expression<Func<IncomingPaymentMetadata, bool>> DueAt(DateTimeOffset now) =>
        x => x.NextActionAtUtc <= now
            && (x.ClaimToken == null || x.ClaimExpiresAtUtc <= now);
}
