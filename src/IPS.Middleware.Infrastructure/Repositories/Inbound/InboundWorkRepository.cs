using System.Linq.Expressions;
using IPS.Middleware.Application.Abstractions.Inbound;
using IPS.Middleware.Application.Inbound;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Persistence;
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
        if (await OwnedAsync(claim, now, cancellationToken) is not { } entry) return false;
        entry.Status = nextActionAtUtc is null ? InboundProcessingStatus.Processed : InboundProcessingStatus.Pending;
        entry.NextActionAtUtc = nextActionAtUtc?.ToUniversalTime();
        Own(entry, null, null);
        return true;
    }

    public async Task<OwnedInboundReceipt?> FindOwnedAsync(InboundClaim claim, DateTimeOffset now, CancellationToken cancellationToken) =>
        await OwnedAsync(claim, now, cancellationToken) is { } entry ? new(entry.Id, entry.ParticipantBic, entry.ReceivedAtUtc) : null;

    public async Task<bool> StageHoldAsync(InboundClaim claim, DateTimeOffset now, string reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(reason.Trim().Length, InboundJournalEntry.HoldReasonLimit, nameof(reason));
        if (await OwnedAsync(claim, now, cancellationToken) is not { } entry) return false;
        entry.Status = InboundProcessingStatus.Held;
        entry.HoldReason = reason.Trim();
        entry.NextActionAtUtc = null;
        Own(entry, null, null);
        return true;
    }

    public async Task<bool> StageOriginalReferencesAsync(InboundClaim claim, IncomingPacs008Reference original,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await OwnedAsync(claim, now, cancellationToken) is not { } entry) return false;
        if (entry.OriginalJson is { } stored)
            return IncomingPaymentJson.Read<IncomingPacs008Reference>(stored) == original
                ? true : throw new InvalidOperationException("The receipt's original references cannot be replaced.");
        entry.OriginalJson = IncomingPaymentJson.Write(original);
        db.AuthorizedInboundWork.Add(entry.Id);
        return true;
    }

    public async Task<bool> StageAttachmentAsync(InboundClaim claim, Guid paymentId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await OwnedAsync(claim, now, cancellationToken) is not { } entry) return false;
        if (entry.OriginalJson is null) throw new InvalidOperationException("Save original references before attaching a receipt.");
        if (entry.IncomingPaymentId is { } attached)
            return attached == paymentId ? true : throw new InvalidOperationException("The receipt is attached to another payment.");
        entry.IncomingPaymentId = paymentId;
        db.AuthorizedInboundWork.Add(entry.Id);
        return true;
    }

    // Discovery and acquisition share one definition: pending, due and without a live owner.
    private static Expression<Func<InboundJournalEntry, bool>> DueAt(DateTimeOffset now) =>
        e => e.Status == InboundProcessingStatus.Pending && e.NextActionAtUtc <= now && (e.ClaimToken == null || e.ClaimExpiresAtUtc <= now);

    private async Task<InboundJournalEntry?> OwnedAsync(InboundClaim claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        var entry = await db.InboundJournal.FindAsync([claim.JournalId], cancellationToken);
        return entry is not null && entry.IsOwnedBy(claim, now) ? entry : null;
    }

    private void Own(InboundJournalEntry entry, Guid? token, DateTimeOffset? expiresAtUtc)
    {
        entry.ClaimToken = token;
        entry.ClaimExpiresAtUtc = expiresAtUtc;
        db.AuthorizedInboundWork.Add(entry.Id);
    }
}
