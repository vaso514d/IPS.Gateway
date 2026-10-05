using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class InboundReceiptRepository(TransactionDbContext db) : IInboundReceiptRepository
{
    public async Task<InboundRegistration> StageRegistrationAsync(InboundReceipt receipt, CancellationToken cancellationToken)
    {
        // Only a positive sequence identifies a delivery; anything else is stored held, never deduplicated or processed.
        var held = receipt.Sequence is not > 0;
        if (!held && await db.InboundJournal.SingleOrDefaultAsync(
                e => e.ParticipantBic == receipt.ParticipantBic && e.Sequence == receipt.Sequence, cancellationToken) is { } existing)
        {
            existing.DuplicateCount = checked(existing.DuplicateCount + 1);
            if (existing.LastDuplicateAtUtc is null || existing.LastDuplicateAtUtc < receipt.ReceivedAtUtc)
            {
                existing.LastDuplicateAtUtc = receipt.ReceivedAtUtc;
            }

            return new(existing.Id, false, existing.Status);
        }

        var entry = new InboundJournalEntry
        {
            Id = Guid.NewGuid(),
            ParticipantBic = receipt.ParticipantBic,
            Sequence = receipt.Sequence,
            MessageType = receipt.MessageType,
            RawXml = receipt.RawXml,
            PossibleDuplicate = receipt.PossibleDuplicate,
            ReceivedAtUtc = receipt.ReceivedAtUtc,
            Status = held ? InboundProcessingStatus.Held : InboundProcessingStatus.Pending,
            HoldReason = held ? "Missing or nonpositive IPS sequence." : null,
            NextActionAtUtc = held ? null : receipt.ReceivedAtUtc
        };
        db.InboundJournal.Add(entry);
        return new(entry.Id, true, entry.Status);
    }

    public async Task<IncomingPacs008Reference?> ReadOriginalReferencesAsync(Guid journalId, CancellationToken cancellationToken)
    {
        var json = await db.InboundJournal.AsNoTracking().Where(e => e.Id == journalId)
            .Select(e => e.OriginalJson).SingleOrDefaultAsync(cancellationToken);
        return json is null ? null : IncomingPaymentJson.Read<IncomingPacs008Reference>(json);
    }

    public async Task<StoredInboundReceipt?> ReadAsync(Guid journalId, CancellationToken cancellationToken)
    {
        var entry = await db.InboundJournal.AsNoTracking().SingleOrDefaultAsync(e => e.Id == journalId, cancellationToken);
        return entry?.Snapshot();
    }
}
