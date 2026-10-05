using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.StatusDelivery;

public sealed class OutgoingStatus
{
    [System.Text.Json.Serialization.JsonConstructor]
    public OutgoingStatus(
        Guid paymentId,
        int sequence,
        string messageType,
        string clientReference,
        TransactionStatus status,
        DateTimeOffset statusAtUtc,
        PaymentDetails details,
        string? messageId,
        string? endToEndId)
    {
        PaymentId = paymentId;
        Sequence = sequence;
        MessageType = messageType;
        ClientReference = clientReference;
        Status = status;
        StatusAtUtc = statusAtUtc;
        Details = details;
        MessageId = messageId;
        EndToEndId = endToEndId;
    }

    public Guid PaymentId { get; init; }
    public int Sequence { get; init; }
    public string MessageType { get; init; }
    public string ClientReference { get; init; }
    public TransactionStatus Status { get; init; }
    public DateTimeOffset StatusAtUtc { get; init; }
    public PaymentDetails Details { get; init; }
    public string? MessageId { get; init; }
    public string? EndToEndId { get; init; }

    public OutgoingStatus(OutgoingStatus original)
    {
        PaymentId = original.PaymentId;
        Sequence = original.Sequence;
        MessageType = original.MessageType;
        ClientReference = original.ClientReference;
        Status = original.Status;
        StatusAtUtc = original.StatusAtUtc;
        Details = original.Details;
        MessageId = original.MessageId;
        EndToEndId = original.EndToEndId;
    }

    public bool IsReportable => Status is TransactionStatus.Accepted or TransactionStatus.Rejected or TransactionStatus.NotSent
        or TransactionStatus.ManualReview or TransactionStatus.ManuallyResolved;
    public string IdempotencyKey => $"{ClientReference}:{Status}";
}

public sealed class StatusDeliveryKey : IEquatable<StatusDeliveryKey>
{
    public StatusDeliveryKey(Guid paymentId, int sequence)
    {
        PaymentId = paymentId;
        Sequence = sequence;
    }

    public Guid PaymentId { get; init; }
    public int Sequence { get; init; }

    public bool Equals(StatusDeliveryKey? other) => other is not null &&
            Equals(PaymentId, other.PaymentId) &&
            Equals(Sequence, other.Sequence);
    public override bool Equals(object? obj) => obj is StatusDeliveryKey other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PaymentId);
        hash.Add(Sequence);
        return hash.ToHashCode();
    }

    public static bool operator ==(StatusDeliveryKey? left, StatusDeliveryKey? right) => Equals(left, right);
    public static bool operator !=(StatusDeliveryKey? left, StatusDeliveryKey? right) => !Equals(left, right);
}

public enum StatusDeliveryState
{
    Pending,
    Delivered,
    Exhausted
}

public sealed class StatusDeliveryWork
{
    public StatusDeliveryWork(
        StatusDeliveryKey key,
        OutgoingStatus status,
        StatusDeliveryState state,
        int attempts,
        DateTimeOffset? nextAtUtc,
        Guid? claimToken,
        DateTimeOffset? claimExpiresAtUtc)
    {
        Key = key;
        Status = status;
        State = state;
        Attempts = attempts;
        NextAtUtc = nextAtUtc;
        ClaimToken = claimToken;
        ClaimExpiresAtUtc = claimExpiresAtUtc;
    }

    public StatusDeliveryKey Key { get; init; }
    public OutgoingStatus Status { get; init; }
    public StatusDeliveryState State { get; init; }
    public int Attempts { get; init; }
    public DateTimeOffset? NextAtUtc { get; init; }
    public Guid? ClaimToken { get; init; }
    public DateTimeOffset? ClaimExpiresAtUtc { get; init; }
}

public sealed class StatusDeliveryRetry
{
    public StatusDeliveryRetry(StatusDeliveryState state, DateTimeOffset? nextAtUtc)
    {
        State = state;
        NextAtUtc = nextAtUtc;
    }

    public StatusDeliveryState State { get; init; }
    public DateTimeOffset? NextAtUtc { get; init; }
}

public enum StatusDeliveryResult
{
    Unavailable,
    Scheduled,
    Delivered,
    Exhausted,
    OwnershipLost
}
