namespace IPS.Middleware.Infrastructure.Payments.Pacs008;

public sealed class PaymentMessageContext
{
    public PaymentMessageContext(string messageId, string transactionId, DateTimeOffset envelopeCreatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        MessageId = messageId;
        TransactionId = transactionId;
        EnvelopeCreatedAtUtc = envelopeCreatedAtUtc.ToUniversalTime();
    }

    public string MessageId { get; }
    public string TransactionId { get; }
    public DateTimeOffset EnvelopeCreatedAtUtc { get; }
}
