using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Payments.Pain002;

// Normalized report data and mapping settings fixed at intake; resuming uses these exact values.
public sealed record AcceptedPain002(
    ValidatedPain002 Payment,
    IpsMessageProfile Profile,
    DateTimeOffset EnvelopeCreatedAtUtc) : IAcceptedPayment
{
    // Explicit implementations keep the shared view out of the stored JSON snapshot.
    // The status shows the refused initiation's payment information id.
    string IAcceptedPayment.EndToEndId => Payment.OriginalPaymentInformationId;

    // The source applies no pre-send deadline to pain.002.
    DateTimeOffset? IAcceptedPayment.SubmissionDeadlineUtc => null;

    // The caller supplies the message id, which is also the stored transaction id: a pain.002 has no separate one.
    ProtocolIds? IAcceptedPayment.SuppliedIds => new(Payment.MessageId, Payment.MessageId);

    // IPS's answer is decided by a response header, so nothing in a reply body is matched against this.
    IpsReplyCorrelation IAcceptedPayment.ReplyCorrelation(string messageId, string transactionId) =>
        new(messageId, Payment.OriginalPaymentInformationId, Payment.OriginalPaymentInformationId, PaymentMessageTypes.Pain002Definition);
}
