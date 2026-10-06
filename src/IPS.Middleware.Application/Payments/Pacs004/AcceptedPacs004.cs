using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Payments.Pacs004;

// Normalized return data and mapping settings fixed at intake; resuming uses these exact values.
public sealed record AcceptedPacs004(
    ValidatedPacs004 Payment,
    IpsMessageProfile Profile,
    DateTimeOffset EnvelopeCreatedAtUtc) : IAcceptedPayment
{
    // Explicit implementations keep the shared view out of the stored JSON snapshot.
    // The status shows the returned payment's end-to-end id.
    string IAcceptedPayment.EndToEndId => Payment.Original.EndToEndId;

    // The source applies no pre-send deadline to pacs.004.
    DateTimeOffset? IAcceptedPayment.SubmissionDeadlineUtc => null;

    // The caller supplies the return id, which is both the message id and the transaction id.
    ProtocolIds? IAcceptedPayment.SuppliedIds => new(Payment.ReturnId, Payment.ReturnId);

    // IPS answers a return with the original payment's transaction and end-to-end ids and our message id.
    IpsReplyCorrelation IAcceptedPayment.ReplyCorrelation(string messageId, string transactionId) =>
        new(messageId, Payment.Original.TransactionId, Payment.Original.EndToEndId, PaymentMessageTypes.Pacs004Definition);
}
