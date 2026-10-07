using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Application.Inbound.StatusReports;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Inbound.Composition;

// Turns an owned receipt into a registered payment or transfer, a ready rejection reply, an applied status report, an
// archived recall or cancellation or a held receipt.
public sealed class IncomingReceiptPreparation(
    IInboundReceiptRepository receipts,
    IInboundWorkRepository work,
    IIncomingCompositionRepository composition,
    IncomingPaymentIntake intake,
    IncomingStatusReportProcessing statusReports,
    IncomingTransferRegistration transfers,
    IIncomingReplyRepository replies,
    IIncomingReplyProtocol protocol,
    IUnitOfWork unitOfWork,
    IncomingCompositionOptions options,
    IncomingReplyOptions replyOptions,
    Pacs008ProtocolProfile profile,
    TimeProvider timeProvider)
{
    private static readonly IncomingCompositionResult OwnershipLost = new(IncomingCompositionStatus.OwnershipLost);

    public async Task<IncomingCompositionResult> PrepareAsync(InboundClaim claim, CancellationToken token)
    {
        var now = timeProvider.GetUtcNow();
        if (await work.FindOwnedAsync(claim, now, token) is null)
        {
            return OwnershipLost;
        }

        var state = (await composition.ReadAsync(claim.JournalId, token))!;
        if (state.HasReply || state.PaymentId is not null)
        {
            return await ContinuePreparedAsync(claim, state, now, token);
        }

        var receipt = (await receipts.ReadAsync(claim.JournalId, token))!.Receipt;
        if (receipt.Sequence is not > 0)
        {
            return await HoldAsync(claim, "Missing or nonpositive IPS sequence.", now, token);
        }

        if (PaymentMessageTypes.IsPacs002(receipt.MessageType))
        {
            return await statusReports.ProcessAsync(claim, receipt, token);
        }

        if (PaymentMessageTypes.IsArchivedCancellation(receipt.MessageType))
        {
            return await ArchiveAsync(claim, now, token);
        }

        if (PaymentMessageTypes.IsIncomingTransfer(receipt.MessageType))
        {
            return await transfers.ProcessAsync(claim, receipt, token);
        }

        if (!PaymentMessageTypes.IsPacs008(receipt.MessageType))
        {
            return await HoldAsync(claim, "Unsupported incoming message type.", now, token);
        }

        return protocol.Read(receipt.RawXml, receipt.ReceivedAtUtc) switch
        {
            IncomingPacs008ReadResult.Hold held => await HoldAsync(claim, held.Reason, now, token),
            IncomingPacs008ReadResult.Reject rejected => await PrepareRejectionAsync(claim, receipt, rejected, now, token),
            IncomingPacs008ReadResult.Ready ready => await RegisterAsync(claim, ready.Payment, now, token),
            _ => throw new InvalidOperationException("Unsupported protocol result.")
        };
    }

    // A receipt that already has a payment or reply continues with that work instead of being read again.
    private async Task<IncomingCompositionResult> ContinuePreparedAsync(
        InboundClaim claim,
        IncomingReceiptState state,
        DateTimeOffset now,
        CancellationToken token)
    {
        var nextActionAtUtc = state.HasReply ? now : now + options.ContinuationDelay;
        if (!await work.StageFinishAsync(claim, now, nextActionAtUtc, token))
        {
            return OwnershipLost;
        }

        await unitOfWork.SaveAsync(token);
        var status = state.HasReply ? IncomingCompositionStatus.ReplyReady : IncomingCompositionStatus.Deferred;
        return new IncomingCompositionResult(status, state.PaymentId);
    }

    private async Task<IncomingCompositionResult> PrepareRejectionAsync(
        InboundClaim claim,
        InboundReceipt receipt,
        IncomingPacs008ReadResult.Reject rejected,
        DateTimeOffset now,
        CancellationToken token)
    {
        if (!await work.StageOriginalReferencesAsync(claim, rejected.Original, now, token))
        {
            return OwnershipLost;
        }

        var decision = new IncomingReplyDecision(false, now, rejected.ReasonCode, rejected.Description);
        var envelope = new IncomingReplyEnvelope(
            receipt.ParticipantBic,
            rejected.Original,
            decision,
            IncomingReplyContext.New(now),
            profile,
            replyOptions.MaxAttempts);
        await replies.StageEnvelopeAsync(claim, envelope, now, token);
        if (!await work.StageFinishAsync(claim, now, now, token))
        {
            throw new PersistenceConcurrencyException("Receipt ownership expired.");
        }

        await unitOfWork.SaveAsync(token);
        return new IncomingCompositionResult(IncomingCompositionStatus.ReplyReady);
    }

    private async Task<IncomingCompositionResult> RegisterAsync(InboundClaim claim, IncomingPacs008 payment, DateTimeOffset now, CancellationToken token)
    {
        var registered = await intake.RegisterAndReleaseAsync(claim, payment, now + options.ContinuationDelay, token);
        var status = registered.Outcome switch
        {
            IncomingRegistrationOutcome.Conflict => IncomingCompositionStatus.Held,
            IncomingRegistrationOutcome.LostOwnership => IncomingCompositionStatus.OwnershipLost,
            _ => IncomingCompositionStatus.Deferred
        };
        return new IncomingCompositionResult(status, registered.PaymentId);
    }

    // The stored receipt is the archive: the recall or cancellation is acknowledged to IPS by the receive worker and is not acted on.
    private async Task<IncomingCompositionResult> ArchiveAsync(InboundClaim claim, DateTimeOffset now, CancellationToken token)
    {
        if (!await work.StageFinishAsync(claim, now, null, token))
        {
            return OwnershipLost;
        }

        await unitOfWork.SaveAsync(token);
        return new IncomingCompositionResult(IncomingCompositionStatus.Terminal);
    }

    private async Task<IncomingCompositionResult> HoldAsync(InboundClaim claim, string reason, DateTimeOffset now, CancellationToken token)
    {
        if (!await work.StageHoldAsync(claim, now, reason, token))
        {
            return OwnershipLost;
        }

        await unitOfWork.SaveAsync(token);
        return new IncomingCompositionResult(IncomingCompositionStatus.Held);
    }
}
