namespace IPS.Middleware.Application.Payments.Pacs008;

// Normalized payment data and mapping settings fixed at intake; resuming uses these exact values.
public sealed record AcceptedPacs008(
    ValidatedPacs008 Payment,
    Pacs008ProtocolProfile Profile,
    DateTimeOffset EnvelopeCreatedAtUtc,
    DateTimeOffset SubmissionDeadlineUtc) : IAcceptedPayment
{
    // Explicit implementations keep the shared view out of the stored JSON snapshot.
    string IAcceptedPayment.EndToEndId => Payment.EndToEndId;

    DateTimeOffset? IAcceptedPayment.SubmissionDeadlineUtc => SubmissionDeadlineUtc;

    ProtocolIds? IAcceptedPayment.SuppliedIds => null;

    IpsReplyCorrelation IAcceptedPayment.ReplyCorrelation(string messageId, string transactionId) =>
        new(messageId, transactionId, Payment.EndToEndId, PaymentMessageTypes.Pacs008Definition);
}
