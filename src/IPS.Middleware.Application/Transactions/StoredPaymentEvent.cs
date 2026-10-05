namespace IPS.Middleware.Application.Transactions;

public sealed class StoredPaymentEvent
{
    public StoredPaymentEvent(
        Guid eventId,
        Guid transactionId,
        int sequence,
        string name,
        int schemaVersion,
        DateTimeOffset occurredAtUtc,
        string payloadJson)
    {
        EventId = eventId;
        TransactionId = transactionId;
        Sequence = sequence;
        Name = name;
        SchemaVersion = schemaVersion;
        OccurredAtUtc = occurredAtUtc;
        PayloadJson = payloadJson;
    }

    public Guid EventId { get; init; }
    public Guid TransactionId { get; init; }
    public int Sequence { get; init; }
    public string Name { get; init; }
    public int SchemaVersion { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public string PayloadJson { get; init; }
}
