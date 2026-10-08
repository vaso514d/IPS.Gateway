using System.Linq.Expressions;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class IncomingTransferRepository(TransactionDbContext db) : IIncomingTransferRepository
{
    public async Task<IncomingTransferSnapshot?> FindAsync(string participantBic, string kind, string key, CancellationToken cancellationToken)
    {
        var bic = participantBic.Trim().ToUpperInvariant();
        // SQL equality ignores trailing spaces, so the ordinal match is chosen among the padded candidates.
        var candidates = await db.IncomingTransferMetadata
            .Include(x => x.Transfer)
            .Where(x => x.Transfer.ParticipantBic == bic && x.Transfer.Kind == kind && x.Transfer.Key == key)
            .ToListAsync(cancellationToken);
        return candidates.SingleOrDefault(x => x.Transfer.Key == key) is { } match ? Snapshot(match) : null;
    }

    public async Task<IncomingTransferSnapshot?> ReadAsync(Guid transferId, CancellationToken cancellationToken) =>
        await db.IncomingTransferMetadata
            .Include(x => x.Transfer)
            .SingleOrDefaultAsync(x => x.Id == transferId, cancellationToken) is { } metadata
            ? Snapshot(metadata)
            : null;

    public void Add(IncomingTransfer transfer, IIncomingTransferContent content, DateTimeOffset deadlineUtc) =>
        db.IncomingTransferMetadata.Add(new IncomingTransferMetadata
        {
            Id = transfer.Id,
            Transfer = transfer,
            RequestJson = IncomingTransferJson.Write(content),
            DeadlineUtc = deadlineUtc.ToUniversalTime(),
            NextActionAtUtc = transfer.RegisteredAtUtc
        });

    public async Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) =>
        await db.IncomingTransferMetadata
            .AsNoTracking()
            .Where(DueAt(now))
            .OrderBy(x => x.NextActionAtUtc)
            .ThenBy(x => x.Id)
            .Take(take)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<IncomingTransferClaim?> StageClaimAsync(Guid transferId, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken)
    {
        var metadata = await db.IncomingTransferMetadata
            .Where(DueAt(now))
            .SingleOrDefaultAsync(x => x.Id == transferId, cancellationToken);
        if (metadata is null)
        {
            return null;
        }

        var claim = new IncomingTransferClaim(metadata.Id, Guid.NewGuid(), now.ToUniversalTime() + duration);
        metadata.SetClaim(claim.Token, claim.ExpiresAtUtc);
        return claim;
    }

    public async Task<bool> StageReleaseAsync(IncomingTransferClaim claim, DateTimeOffset now, DateTimeOffset? nextActionAtUtc, CancellationToken cancellationToken)
    {
        var metadata = await db.IncomingTransferMetadata.FindAsync([claim.TransferId], cancellationToken);
        if (metadata is null || !metadata.HasLiveClaim(claim, now))
        {
            return false;
        }

        metadata.NextActionAtUtc = nextActionAtUtc?.ToUniversalTime();
        metadata.SetClaim(null, null);
        return true;
    }

    private static IncomingTransferSnapshot Snapshot(IncomingTransferMetadata metadata) =>
        new(metadata.Transfer, IncomingTransferJson.Read(metadata.Transfer.Kind, metadata.RequestJson), metadata.DeadlineUtc);

    // Discovery and acquisition share one definition: due and without a live owner.
    private static Expression<Func<IncomingTransferMetadata, bool>> DueAt(DateTimeOffset now) =>
        x => x.NextActionAtUtc <= now
            && (x.ClaimToken == null || x.ClaimExpiresAtUtc <= now);
}
