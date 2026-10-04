namespace IPS.Middleware.Application.Payments.Pacs008;

/// <summary>A detached snapshot of the exact protocol values committed for a payment.</summary>
public sealed record PreparedPaymentMessage(
    string MessageId, string TransactionId, string? UnsignedXml, string? SignedXml, AcceptedPacs008? Accepted = null);
