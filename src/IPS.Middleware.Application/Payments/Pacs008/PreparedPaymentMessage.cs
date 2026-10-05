namespace IPS.Middleware.Application.Payments.Pacs008;
/// <summary>A detached snapshot of the exact protocol values committed for a payment.</summary>
public sealed class PreparedPaymentMessage
{
    public PreparedPaymentMessage(
        string messageId,
        string transactionId,
        string? unsignedXml,
        string? signedXml,
        AcceptedPacs008? accepted = null,
        SubmissionMessageKind? readyDisposition = null)
    {
        MessageId = messageId;
        TransactionId = transactionId;
        UnsignedXml = unsignedXml;
        SignedXml = signedXml;
        Accepted = accepted;
        ReadyDisposition = readyDisposition;
    }

    public string MessageId { get; init; }
    public string TransactionId { get; init; }
    public string? UnsignedXml { get; init; }
    public string? SignedXml { get; init; }
    public AcceptedPacs008? Accepted { get; init; }
    public SubmissionMessageKind? ReadyDisposition { get; init; }
}
