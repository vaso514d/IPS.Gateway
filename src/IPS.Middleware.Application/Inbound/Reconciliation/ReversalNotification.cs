namespace IPS.Middleware.Application.Inbound.Reconciliation;

public sealed record ReversalNotification(Guid PaymentId, string ParticipantBic, string EndToEndId,
    DateTimeOffset StatusAtUtc, string? ReasonCode, string? Description, string GroupMessageId);
