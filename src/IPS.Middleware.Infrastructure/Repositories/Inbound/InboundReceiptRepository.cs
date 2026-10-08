using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class InboundReceiptRepository(TransactionDbContext db) : IInboundReceiptRepository
{
    private const string MissingSequenceReason = "Missing or nonpositive IPS sequence.";

    public async Task<InboundRegistration> StageRegistrationAsync(InboundReceipt receipt, CancellationToken cancellationToken)
    {
        // Only a positive sequence identifies a delivery; anything else is stored held, never deduplicated or processed.
        var identifiable = receipt.Sequence > 0;
        if (identifiable && await FindDeliveryAsync(receipt, cancellationToken) is { } existing)
        {
            RecordDuplicate(existing, receipt.ReceivedAtUtc);
            return new InboundRegistration(existing.Id, false, existing.Status);
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
            Status = identifiable ? InboundProcessingStatus.Pending : InboundProcessingStatus.Held,
            HoldReason = identifiable ? null : MissingSequenceReason,
            NextActionAtUtc = identifiable ? receipt.ReceivedAtUtc : null
        };
        db.InboundJournal.Add(entry);
        return new InboundRegistration(entry.Id, true, entry.Status);
    }

    public async Task<IncomingPacs008Reference?> ReadOriginalReferencesAsync(Guid journalId, CancellationToken cancellationToken)
    {
        var json = await db.InboundJournal
            .AsNoTracking()
            .Where(x => x.Id == journalId)
            .Select(x => x.OriginalJson)
            .SingleOrDefaultAsync(cancellationToken);
        return json is null ? null : IncomingPaymentJson.Read<IncomingPacs008Reference>(json);
    }

    public async Task<StoredInboundReceipt?> ReadAsync(Guid journalId, CancellationToken cancellationToken)
    {
        var entry = await db.InboundJournal
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == journalId, cancellationToken);
        return entry?.Snapshot();
    }

    private Task<InboundJournalEntry?> FindDeliveryAsync(InboundReceipt receipt, CancellationToken cancellationToken) =>
        db.InboundJournal.SingleOrDefaultAsync(
            x => x.ParticipantBic == receipt.ParticipantBic && x.Sequence == receipt.Sequence,
            cancellationToken);

    private static void RecordDuplicate(InboundJournalEntry existing, DateTimeOffset receivedAtUtc)
    {
        existing.DuplicateCount = checked(existing.DuplicateCount + 1);
        if (existing.LastDuplicateAtUtc is null || existing.LastDuplicateAtUtc < receivedAtUtc)
        {
            existing.LastDuplicateAtUtc = receivedAtUtc;
        }
    }
}
