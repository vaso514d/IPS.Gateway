using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.StatusDelivery;

public sealed record OutgoingStatus(
    Guid PaymentId,
    int Sequence,
    string MessageType,
    string ClientReference,
    TransactionStatus Status,
    DateTimeOffset StatusAtUtc,
    PaymentDetails Details,
    string? MessageId,
    string? EndToEndId)
{
    public bool IsReportable => Status is TransactionStatus.Accepted
        or TransactionStatus.Rejected
        or TransactionStatus.NotSent
        or TransactionStatus.ManualReview
        or TransactionStatus.ManuallyResolved;

    public string IdempotencyKey => $"{ClientReference}:{Status}";
}

public sealed record StatusDeliveryKey(Guid PaymentId, int Sequence);

public enum StatusDeliveryState
{
    Pending,
    Delivered,
    Exhausted
}

public sealed record StatusDeliveryWork(
    StatusDeliveryKey Key,
    OutgoingStatus Status,
    StatusDeliveryState State,
    int Attempts,
    DateTimeOffset? NextAtUtc,
    Guid? ClaimToken,
    DateTimeOffset? ClaimExpiresAtUtc);

public sealed record StatusDeliveryRetry(StatusDeliveryState State, DateTimeOffset? NextAtUtc);

public enum StatusDeliveryResult
{
    Unavailable,
    Scheduled,
    Delivered,
    Exhausted,
    OwnershipLost
}
