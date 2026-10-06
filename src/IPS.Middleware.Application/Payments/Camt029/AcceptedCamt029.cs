using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Payments.Camt029;

// Normalized answer data and mapping settings fixed at intake; resuming uses these exact values.
public sealed record AcceptedCamt029(
    ValidatedCamt029 Payment,
    IpsMessageProfile Profile,
    DateTimeOffset EnvelopeCreatedAtUtc) : IAcceptedPayment
{
    // Explicit implementations keep the shared view out of the stored JSON snapshot.
    // The status shows the recalled payment's end-to-end id.
    string IAcceptedPayment.EndToEndId => Payment.OriginalEndToEndId;

    // The source applies no pre-send deadline to camt.029.
    DateTimeOffset? IAcceptedPayment.SubmissionDeadlineUtc => null;

    // The caller supplies the message id and the cancellation status id.
    ProtocolIds? IAcceptedPayment.SuppliedIds => new(Payment.MessageId, Payment.CancellationStatusId);

    // IPS answers with the recalled payment's transaction and end-to-end ids and our message id.
    IpsReplyCorrelation IAcceptedPayment.ReplyCorrelation(string messageId, string transactionId) =>
        new(messageId, Payment.OriginalTransactionId, Payment.OriginalEndToEndId, PaymentMessageTypes.Camt029Definition);
}
