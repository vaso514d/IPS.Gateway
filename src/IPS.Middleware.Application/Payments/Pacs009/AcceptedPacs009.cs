using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Payments.Pacs009;

// Normalized payment data and mapping settings fixed at intake; resuming uses these exact values.
public sealed record AcceptedPacs009(
    ValidatedPacs009 Payment,
    IpsMessageProfile Profile,
    DateTimeOffset EnvelopeCreatedAtUtc) : IAcceptedPayment
{
    // Explicit implementations keep the shared view out of the stored JSON snapshot.
    string IAcceptedPayment.EndToEndId => Payment.EndToEndId;

    // The source applies no pre-send deadline to pacs.009.
    DateTimeOffset? IAcceptedPayment.SubmissionDeadlineUtc => null;

    // The caller supplies the message and transaction ids of a pacs.009.
    ProtocolIds? IAcceptedPayment.SuppliedIds => new(Payment.MessageId, Payment.TransactionId);

    IpsReplyCorrelation IAcceptedPayment.ReplyCorrelation(string messageId, string transactionId) =>
        new(messageId, transactionId, Payment.EndToEndId, PaymentMessageTypes.Pacs009Definition);
}
