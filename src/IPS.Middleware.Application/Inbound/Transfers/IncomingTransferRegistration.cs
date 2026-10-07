using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Composition;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Transfers;

// Registers the verified transfer (a pacs.009 or a pacs.004 return) of an owned receipt for the core system. IPS is told
// nothing beyond the receipt, so the receipt is complete once the transfer is stored; delivery and recovery are the
// transfer's own work. Anything unverifiable, misaddressed or conflicting holds the receipt and delivers nothing.
public sealed class IncomingTransferRegistration(
    IEnumerable<IIncomingTransferProtocol> protocols,
    IIncomingTransferRepository transfers,
    IInboundWorkRepository receiptWork,
    IUnitOfWork unitOfWork,
    IncomingReconciliationOptions options,
    TimeProvider timeProvider)
{
    public const string ConflictReason = "Transfer identity conflict: contents differ from the registered transfer.";
    private const string NotOurs = "The transfer is not addressed to our participant.";

    private static readonly IncomingCompositionResult OwnershipLost = new(IncomingCompositionStatus.OwnershipLost);

    // Uniqueness and concurrency failures propagate and fail this scope. A fresh attempt reuses the persisted winner
    // only when its contents match; otherwise the receipt is held.
    public async Task<IncomingCompositionResult> ProcessAsync(InboundClaim claim, InboundReceipt receipt, CancellationToken token)
    {
        var now = timeProvider.GetUtcNow();
        var read = protocols.Single(protocol => protocol.Reads(receipt.MessageType)).Read(receipt.RawXml, receipt.ReceivedAtUtc);
        if (read is not IncomingTransferReadResult.Ready { Transfer: var content })
        {
            return await HoldAsync(claim, ((IncomingTransferReadResult.Hold)read).Reason, now, token);
        }

        if (!string.Equals(content.ReceiverBic, receipt.ParticipantBic, StringComparison.OrdinalIgnoreCase))
        {
            return await HoldAsync(claim, NotOurs, now, token);
        }

        var existing = await transfers.FindAsync(receipt.ParticipantBic, content.Kind, content.Key, token);
        if (existing is not null && !existing.Content.Equals(content))
        {
            return await HoldAsync(claim, ConflictReason, now, token);
        }

        var transfer = existing?.Transfer ?? IncomingTransfer.Register(Guid.NewGuid(), receipt.ParticipantBic, content.Kind, content.Key, now);
        if (existing is null)
        {
            transfers.Add(transfer, content, now + options.Window);
        }

        return await CompleteAsync(claim, transfer.Id, now, token);
    }

    private async Task<IncomingCompositionResult> CompleteAsync(InboundClaim claim, Guid transferId, DateTimeOffset now, CancellationToken token)
    {
        if (!await receiptWork.StageFinishAsync(claim, now, null, token))
        {
            return OwnershipLost;
        }

        await unitOfWork.SaveAsync(token);
        return new IncomingCompositionResult(IncomingCompositionStatus.Terminal, transferId);
    }

    private async Task<IncomingCompositionResult> HoldAsync(InboundClaim claim, string reason, DateTimeOffset now, CancellationToken token)
    {
        if (!await receiptWork.StageHoldAsync(claim, now, reason, token))
        {
            return OwnershipLost;
        }

        await unitOfWork.SaveAsync(token);
        return new IncomingCompositionResult(IncomingCompositionStatus.Held);
    }
}
