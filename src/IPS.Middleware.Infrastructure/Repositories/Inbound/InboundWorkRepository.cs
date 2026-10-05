using System.Linq.Expressions;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class InboundWorkRepository(TransactionDbContext db) : IInboundWorkRepository
{
    public async Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) =>
        await db.InboundJournal
            .AsNoTracking()
            .Where(DueAt(now))
            .OrderBy(x => x.NextActionAtUtc)
            .ThenBy(x => x.ReceivedAtUtc)
            .ThenBy(x => x.Id)
            .Take(take)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<InboundClaim?> StageClaimAsync(Guid journalId, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken)
    {
        var entry = await db.InboundJournal
            .Where(DueAt(now))
            .SingleOrDefaultAsync(x => x.Id == journalId, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        var claim = new InboundClaim(entry.Id, Guid.NewGuid(), now.ToUniversalTime() + duration);
        SetClaim(entry, claim.Token, claim.ExpiresAtUtc);
        return claim;
    }

    public async Task<bool> StageFinishAsync(InboundClaim claim, DateTimeOffset now, DateTimeOffset? nextActionAtUtc, CancellationToken cancellationToken)
    {
        if (await OwnedAsync(claim, now, cancellationToken) is not { } entry)
        {
            return false;
        }

        entry.Status = nextActionAtUtc is null ? InboundProcessingStatus.Processed : InboundProcessingStatus.Pending;
        entry.NextActionAtUtc = nextActionAtUtc?.ToUniversalTime();
        SetClaim(entry, null, null);
        return true;
    }

    public async Task<OwnedInboundReceipt?> FindOwnedAsync(InboundClaim claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entry = await OwnedAsync(claim, now, cancellationToken);
        return entry is null ? null : new OwnedInboundReceipt(entry.Id, entry.ParticipantBic, entry.ReceivedAtUtc);
    }

    public async Task<bool> StageHoldAsync(InboundClaim claim, DateTimeOffset now, string reason, CancellationToken cancellationToken)
    {
        // Reasons may carry protocol detail; the column has a fixed limit.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(reason.Trim().Length, InboundJournalEntry.HoldReasonLimit, nameof(reason));
        if (await OwnedAsync(claim, now, cancellationToken) is not { } entry)
        {
            return false;
        }

        entry.Status = InboundProcessingStatus.Held;
        entry.HoldReason = reason.Trim();
        entry.NextActionAtUtc = null;
        SetClaim(entry, null, null);
        return true;
    }

    public async Task<bool> StageOriginalReferencesAsync(
        InboundClaim claim,
        IncomingPacs008Reference original,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await OwnedAsync(claim, now, cancellationToken) is not { } entry)
        {
            return false;
        }

        if (entry.OriginalJson is { } stored)
        {
            if (IncomingPaymentJson.Read<IncomingPacs008Reference>(stored) != original)
            {
                throw new InvalidOperationException("The receipt's original references cannot be replaced.");
            }

            return true;
        }

        entry.OriginalJson = IncomingPaymentJson.Write(original);
        return true;
    }

    public async Task<bool> StageAttachmentAsync(InboundClaim claim, Guid paymentId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await OwnedAsync(claim, now, cancellationToken) is not { } entry)
        {
            return false;
        }

        if (entry.OriginalJson is null)
        {
            throw new InvalidOperationException("Save original references before attaching a receipt.");
        }

        if (entry.IncomingPaymentId is { } attached && attached != paymentId)
        {
            throw new InvalidOperationException("The receipt is attached to another payment.");
        }

        entry.IncomingPaymentId = paymentId;
        return true;
    }

    // Discovery and acquisition share one definition: pending, due and without a live owner.
    internal static Expression<Func<InboundJournalEntry, bool>> DueAt(DateTimeOffset now) =>
        x => x.Status == InboundProcessingStatus.Pending
            && x.NextActionAtUtc <= now
            && (x.ClaimToken == null || x.ClaimExpiresAtUtc <= now);

    private async Task<InboundJournalEntry?> OwnedAsync(InboundClaim claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entry = await db.InboundJournal.FindAsync([claim.JournalId], cancellationToken);
        return entry is not null && entry.IsOwnedBy(claim, now) ? entry : null;
    }

    private static void SetClaim(InboundJournalEntry entry, Guid? token, DateTimeOffset? expiresAtUtc)
    {
        entry.ClaimToken = token;
        entry.ClaimExpiresAtUtc = expiresAtUtc;
    }
}
