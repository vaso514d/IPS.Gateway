namespace IPS.Middleware.Application.Payments.Pacs008;

// A detached snapshot of the exact protocol values committed for a payment.
public sealed record PreparedPaymentMessage(
    string MessageId,
    string TransactionId,
    string? UnsignedXml,
    string? SignedXml,
    IAcceptedPayment? Accepted = null,
    SubmissionMessageKind? ReadyDisposition = null);
