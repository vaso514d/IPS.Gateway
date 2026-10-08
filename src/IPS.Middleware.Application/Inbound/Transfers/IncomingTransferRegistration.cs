using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Composition;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Transfers;

// Registers the verified transfer of an owned receipt for the core system. IPS is told nothing beyond the receipt, so the
// receipt is complete once the transfer is stored; delivery and recovery are the transfer's own work. A refusal of our
// recall is registered only when it matches that recall, and the refusal is recorded on the recall in the same commit.
// Anything unverifiable, misaddressed, unmatched or conflicting holds the receipt and delivers nothing.
public sealed class IncomingTransferRegistration(
    IEnumerable<IIncomingTransferProtocol> protocols,
    IIncomingTransferRepository transfers,
    IncomingRecallRefusals refusals,
    IInboundWorkRepository receiptWork,
    IUnitOfWork unitOfWork,
    IncomingReconciliationOptions options,
    IncomingCompositionOptions composition,
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

        var match = content is IncomingCamt029 refusal ? await refusals.MatchAsync(refusal, token) : null;
        if (match is RecallRefusalMatch.Unmatched unmatched)
        {
            return await HoldAsync(claim, unmatched.Reason, now, token);
        }

        if (match is RecallRefusalMatch.Matched matched)
        {
            content = matched.Refusal;
        }

        var existing = await transfers.FindAsync(receipt.ParticipantBic, content.Kind, content.Key, token);
        if (existing is not null && !existing.Content.Equals(content))
        {
            return await HoldAsync(claim, ConflictReason, now, token);
        }

        if (existing is not null)
        {
            return await CompleteAsync(claim, existing.Transfer.Id, now, token);
        }

        // A redelivered refusal finds its transfer already stored, committed together with the recorded refusal.
        if (match is RecallRefusalMatch.Matched recall && !refusals.TryRecord(recall, now))
        {
            return await DeferAsync(claim, now, token);
        }

        var transfer = IncomingTransfer.Register(Guid.NewGuid(), receipt.ParticipantBic, content.Kind, content.Key, now);
        transfers.Add(transfer, content, now + options.Window);
        return await CompleteAsync(claim, transfer.Id, now, token);
    }

    private async Task<IncomingCompositionResult> DeferAsync(InboundClaim claim, DateTimeOffset now, CancellationToken token)
    {
        if (!await receiptWork.StageFinishAsync(claim, now, now + composition.ContinuationDelay, token))
        {
            return OwnershipLost;
        }

        await unitOfWork.SaveAsync(token);
        return new IncomingCompositionResult(IncomingCompositionStatus.Deferred);
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
