namespace IPS.Middleware.Application.Transactions;

public sealed record StoredPaymentEvent(
    Guid EventId, Guid TransactionId, int Sequence, string Name, int SchemaVersion,
    DateTimeOffset OccurredAtUtc, string PayloadJson);
