using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Payments.Camt056;

// Normalized recall data and mapping settings fixed at intake; resuming uses these exact values.
public sealed record AcceptedCamt056(
    ValidatedCamt056 Payment,
    IpsMessageProfile Profile,
    DateTimeOffset EnvelopeCreatedAtUtc) : IAcceptedPayment
{
    // Explicit implementations keep the shared view out of the stored JSON snapshot.
    // The status shows the recalled payment's end-to-end id.
    string IAcceptedPayment.EndToEndId => Payment.OriginalEndToEndId;

    // The source applies no pre-send deadline to camt.056.
    DateTimeOffset? IAcceptedPayment.SubmissionDeadlineUtc => null;

    // The caller supplies the message id and the recall id.
    ProtocolIds? IAcceptedPayment.SuppliedIds => new(Payment.MessageId, Payment.RecallId);

    // IPS answers a recall with the recalled payment's transaction and end-to-end ids and our message id.
    IpsReplyCorrelation IAcceptedPayment.ReplyCorrelation(string messageId, string transactionId) =>
        new(messageId, Payment.OriginalTransactionId, Payment.OriginalEndToEndId, PaymentMessageTypes.Camt056Definition);
}
