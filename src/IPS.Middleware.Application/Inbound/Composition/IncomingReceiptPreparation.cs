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

// Turns an owned receipt into a registered payment or transfer (recalls, recall refusals and cancellations included), a
// ready rejection reply, an applied status report or a held receipt.
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
    // ISO 20022 external status reason "Invalid creditor BIC identifier".
    private const string InvalidCreditorAgent = "RC07";
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

        if (PaymentMessageTypes.IsIncomingTransfer(receipt.MessageType))
        {
            return await transfers.ProcessAsync(claim, receipt, token);
        }

        if (!PaymentMessageTypes.IsPacs008(receipt.MessageType))
        {
            return await HoldAsync(claim, "Unsupported incoming message type.", now, token);
        }

        return AddressedTo(receipt.ParticipantBic, protocol.Read(receipt.RawXml, receipt.ReceivedAtUtc)) switch
        {
            IncomingPacs008ReadResult.Hold held => await HoldAsync(claim, held.Reason, now, token),
            IncomingPacs008ReadResult.Reject rejected => await PrepareRejectionAsync(claim, receipt, rejected, now, token),
            IncomingPacs008ReadResult.Ready ready => await RegisterAsync(claim, receipt, ready.Payment, now, token),
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

    // The receipt participant is our configured bank code. A trusted payment whose creditor agent is another bank is
    // rejected rather than credited (decision 016); IPS is not expected to route one here.
    private static IncomingPacs008ReadResult AddressedTo(string participantBic, IncomingPacs008ReadResult read)
    {
        if (read is not IncomingPacs008ReadResult.Ready { Payment: var payment })
        {
            return read;
        }

        var creditorAgent = payment.Payment.Creditor?.ParticipantBic?.Trim();
        if (string.Equals(creditorAgent, participantBic, StringComparison.OrdinalIgnoreCase))
        {
            return read;
        }

        return new IncomingPacs008ReadResult.Reject(payment.Original, InvalidCreditorAgent, $"Creditor agent '{creditorAgent}' is not this bank.");
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

    // A duplicate EndToEndId is rejected at once (AM05), so the sender learns it now rather than at the IPS deadline.
    private async Task<IncomingCompositionResult> RegisterAsync(
        InboundClaim claim,
        InboundReceipt receipt,
        IncomingPacs008 payment,
        DateTimeOffset now,
        CancellationToken token)
    {
        var registered = await intake.RegisterAndReleaseAsync(claim, payment, now + options.ContinuationDelay, token);
        if (registered.Outcome == IncomingRegistrationOutcome.Conflict)
        {
            var duplicate = new IncomingPacs008ReadResult.Reject(payment.Original, IncomingPaymentIntake.DuplicateReasonCode, IncomingPaymentIntake.ConflictReason);
            return await PrepareRejectionAsync(claim, receipt, duplicate, now, token);
        }

        var status = registered.Outcome == IncomingRegistrationOutcome.LostOwnership
            ? IncomingCompositionStatus.OwnershipLost
            : IncomingCompositionStatus.Deferred;
        return new IncomingCompositionResult(status, registered.PaymentId);
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
