using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Composition;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Inbound.StatusReports;

// Applies a verified pacs.002 that IPS sent on its own to the outgoing payment it names. The payment change, its
// callback and the receipt completion commit together; anything unverified or unmatched holds the receipt unchanged.
public sealed class IncomingStatusReportProcessing(
    IOutgoingPaymentRepository payments,
    IPaymentPreparationRepository preparation,
    ITransactionWorkRepository paymentWork,
    IInboundWorkRepository receiptWork,
    IStatusReportProtocol protocol,
    IUnitOfWork unitOfWork,
    IncomingCompositionOptions options,
    TimeProvider timeProvider)
{
    private const string NotAReport = "The message is not a readable pacs.002 status report.";
    private const string UnknownPayment = "No outgoing payment has the original message id of this report.";
    private const string PaymentDataUnavailable = "Accepted payment data is unavailable, so the report cannot be verified.";

    private static readonly IncomingCompositionResult OwnershipLost = new(IncomingCompositionStatus.OwnershipLost);

    public async Task<IncomingCompositionResult> ProcessAsync(InboundClaim claim, InboundReceipt receipt, CancellationToken token)
    {
        var now = timeProvider.GetUtcNow();
        var originalMessageId = protocol.OriginalMessageId(receipt.RawXml);
        if (originalMessageId is null)
        {
            return await HoldAsync(claim, NotAReport, now, token);
        }

        var payment = await payments.FindByMessageIdAsync(originalMessageId, token);
        if (payment is null)
        {
            return await HoldAsync(claim, UnknownPayment, now, token);
        }

        var message = await preparation.ReadAsync(payment.Id, token);
        if (message?.Accepted is not { } accepted)
        {
            return await HoldAsync(claim, PaymentDataUnavailable, now, token);
        }

        var correlation = new IpsReplyCorrelation(
            message.MessageId,
            message.TransactionId,
            accepted.EndToEndId,
            PaymentMessageTypes.DefinitionOf(payment.MessageType));
        var reply = protocol.Interpret(receipt.RawXml, correlation);
        if (reply.Status == IpsReplyStatus.Unresolved)
        {
            return await HoldAsync(claim, reply.Details.Description ?? NotAReport, now, token);
        }

        // Only a payment awaiting its outcome is settled. One under a live claim is being processed by its owner, so
        // try again once that owner is done. Any other payment only records the observation.
        if (payment.AwaitsOutcome)
        {
            if (!paymentWork.StageSettlement(payment, now))
            {
                return await DeferAsync(claim, now, token);
            }
        }
        else
        {
            paymentWork.StageObservation(payment);
        }

        var reported = reply.Status == IpsReplyStatus.Accepted ? TransactionStatus.Accepted : TransactionStatus.Rejected;
        payment.RecordReport(reported, StatusSource.Ips, now, reply.Details);
        return await CompleteAsync(claim, payment.Id, now, token);
    }

    private async Task<IncomingCompositionResult> CompleteAsync(InboundClaim claim, Guid paymentId, DateTimeOffset now, CancellationToken token)
    {
        if (!await receiptWork.StageFinishAsync(claim, now, null, token))
        {
            return OwnershipLost;
        }

        await unitOfWork.SaveAsync(token);
        return new IncomingCompositionResult(IncomingCompositionStatus.Terminal, paymentId);
    }

    private async Task<IncomingCompositionResult> DeferAsync(InboundClaim claim, DateTimeOffset now, CancellationToken token)
    {
        if (!await receiptWork.StageFinishAsync(claim, now, now + options.ContinuationDelay, token))
        {
            return OwnershipLost;
        }

        await unitOfWork.SaveAsync(token);
        return new IncomingCompositionResult(IncomingCompositionStatus.Deferred);
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
