using System.Linq.Expressions;
using IPS.Middleware.Application.Abstractions.Inbound;
using IPS.Middleware.Application.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class InboundWorkRepository(TransactionDbContext db) : IInboundWorkRepository
{
    public async Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        return await db.InboundJournal.AsNoTracking().Where(DueAt(now))
            .OrderBy(e => e.NextActionAtUtc).ThenBy(e => e.ReceivedAtUtc).ThenBy(e => e.Id)
            .Take(take).Select(e => e.Id).ToListAsync(cancellationToken);
    }

    public async Task<InboundClaim?> StageClaimAsync(Guid journalId, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        var entry = await db.InboundJournal.Where(DueAt(now)).SingleOrDefaultAsync(e => e.Id == journalId, cancellationToken);
        if (entry is null) return null;
        var claim = new InboundClaim(entry.Id, Guid.NewGuid(), now.ToUniversalTime() + duration);
        Own(entry, claim.Token, claim.ExpiresAtUtc);
        return claim;
    }

    public async Task<bool> StageFinishAsync(InboundClaim claim, DateTimeOffset now, DateTimeOffset? nextActionAtUtc, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        var entry = await db.InboundJournal.FindAsync([claim.JournalId], cancellationToken);
        if (entry is not { Status: InboundProcessingStatus.Pending } || entry.ClaimToken != claim.Token || entry.ClaimExpiresAtUtc <= now)
            return false;
        entry.Status = nextActionAtUtc is null ? InboundProcessingStatus.Processed : InboundProcessingStatus.Pending;
        entry.NextActionAtUtc = nextActionAtUtc?.ToUniversalTime();
        Own(entry, null, null);
        return true;
    }

    // Discovery and acquisition share one definition: pending, due and without a live owner.
    private static Expression<Func<InboundJournalEntry, bool>> DueAt(DateTimeOffset now) =>
        e => e.Status == InboundProcessingStatus.Pending && e.NextActionAtUtc <= now && (e.ClaimToken == null || e.ClaimExpiresAtUtc <= now);

    private void Own(InboundJournalEntry entry, Guid? token, DateTimeOffset? expiresAtUtc)
    {
        entry.ClaimToken = token;
        entry.ClaimExpiresAtUtc = expiresAtUtc;
        db.AuthorizedInboundWork.Add(entry.Id);
    }
}
