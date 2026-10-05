namespace IPS.Middleware.Domain.Transactions;

public enum PaymentOperation
{
    BeginSending,
    Accept,
    Reject,
    RetryConnection,
    NotSent,
    OutcomeUnknown,
    BeginInvestigation,
    BeginResending,
    RequireManualReview,
    ResolveManually
}

public sealed class PaymentDetails : IEquatable<PaymentDetails>
{
    public PaymentDetails(string? reasonCode = null, int? ipsInternalCode = null, string? description = null)
    {
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? null : reasonCode.Trim().ToUpperInvariant();
        IpsInternalCode = ipsInternalCode;
        var text = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Description = text is { Length: > 2000 } ? text[..2000] : text;
    }

    public string? ReasonCode { get; }
    public int? IpsInternalCode { get; }
    public string? Description { get; }

    public bool Equals(PaymentDetails? other) => other is not null &&
            Equals(ReasonCode, other.ReasonCode) &&
            Equals(IpsInternalCode, other.IpsInternalCode) &&
            Equals(Description, other.Description);
    public override bool Equals(object? obj) => obj is PaymentDetails other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ReasonCode);
        hash.Add(IpsInternalCode);
        hash.Add(Description);
        return hash.ToHashCode();
    }

    public static bool operator ==(PaymentDetails? left, PaymentDetails? right) => Equals(left, right);
    public static bool operator !=(PaymentDetails? left, PaymentDetails? right) => !Equals(left, right);
}

public sealed class PaymentOutcome : IEquatable<PaymentOutcome>
{
    public PaymentOutcome(TransactionStatus status, StatusSource source, DateTimeOffset atUtc, int sequence, PaymentDetails details)
    {
        Status = status;
        Source = source;
        AtUtc = atUtc;
        Sequence = sequence;
        Details = details;
    }

    public TransactionStatus Status { get; init; }
    public StatusSource Source { get; init; }
    public DateTimeOffset AtUtc { get; init; }
    public int Sequence { get; init; }
    public PaymentDetails Details { get; init; }

    public bool Equals(PaymentOutcome? other) => other is not null &&
            Equals(Status, other.Status) &&
            Equals(Source, other.Source) &&
            Equals(AtUtc, other.AtUtc) &&
            Equals(Sequence, other.Sequence) &&
            Equals(Details, other.Details);
    public override bool Equals(object? obj) => obj is PaymentOutcome other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Status);
        hash.Add(Source);
        hash.Add(AtUtc);
        hash.Add(Sequence);
        hash.Add(Details);
        return hash.ToHashCode();
    }

    public static bool operator ==(PaymentOutcome? left, PaymentOutcome? right) => Equals(left, right);
    public static bool operator !=(PaymentOutcome? left, PaymentOutcome? right) => !Equals(left, right);
}

public sealed class PaymentReceived : DomainEvent
{
    public PaymentReceived(
        Guid eventId,
        Guid aggregateId,
        int sequence,
        DateTimeOffset occurredAtUtc,
        string messageType,
        string clientReference) : base(eventId, aggregateId, sequence, occurredAtUtc)
    {
        MessageType = messageType;
        ClientReference = clientReference;
    }

    public string MessageType { get; init; }
    public string ClientReference { get; init; }
}

public sealed class PaymentStateChanged : DomainEvent
{
    public PaymentStateChanged(
        Guid eventId,
        Guid aggregateId,
        int sequence,
        DateTimeOffset occurredAtUtc,
        PaymentOperation operation,
        TransactionStatus previousStatus,
        TransactionStatus status,
        StatusSource source,
        PaymentDetails details) : base(eventId, aggregateId, sequence, occurredAtUtc)
    {
        Operation = operation;
        PreviousStatus = previousStatus;
        Status = status;
        Source = source;
        Details = details;
    }

    public PaymentOperation Operation { get; init; }
    public TransactionStatus PreviousStatus { get; init; }
    public TransactionStatus Status { get; init; }
    public StatusSource Source { get; init; }
    public PaymentDetails Details { get; init; }
}

public sealed class PaymentProcessingObserved : DomainEvent
{
    public PaymentProcessingObserved(
        Guid eventId,
        Guid aggregateId,
        int sequence,
        DateTimeOffset occurredAtUtc,
        ProcessingStep step,
        TransactionStatus status) : base(eventId, aggregateId, sequence, occurredAtUtc)
    {
        Step = step;
        Status = status;
    }

    public ProcessingStep Step { get; init; }
    public TransactionStatus Status { get; init; }
}

public sealed class PaymentProcessingFailed : DomainEvent
{
    public PaymentProcessingFailed(
        Guid eventId,
        Guid aggregateId,
        int sequence,
        DateTimeOffset occurredAtUtc,
        ProcessingStep step,
        TransactionStatus status,
        string description) : base(eventId, aggregateId, sequence, occurredAtUtc)
    {
        Step = step;
        Status = status;
        Description = description;
    }

    public ProcessingStep Step { get; init; }
    public TransactionStatus Status { get; init; }
    public string Description { get; init; }
}

public sealed class PaymentOutcomeObserved : DomainEvent
{
    public PaymentOutcomeObserved(
        Guid eventId,
        Guid aggregateId,
        int sequence,
        DateTimeOffset occurredAtUtc,
        TransactionStatus currentStatus,
        TransactionStatus reportedStatus,
        bool conflicting,
        StatusSource source,
        PaymentDetails details) : base(eventId, aggregateId, sequence, occurredAtUtc)
    {
        CurrentStatus = currentStatus;
        ReportedStatus = reportedStatus;
        Conflicting = conflicting;
        Source = source;
        Details = details;
    }

    public TransactionStatus CurrentStatus { get; init; }
    public TransactionStatus ReportedStatus { get; init; }
    public bool Conflicting { get; init; }
    public StatusSource Source { get; init; }
    public PaymentDetails Details { get; init; }
}

public sealed class PaymentTransitionException(TransactionStatus state, PaymentOperation operation) : InvalidOperationException($"Payment operation {operation} is not valid in state {state}.")
{
    public TransactionStatus State { get; } = state;
    public PaymentOperation Operation { get; } = operation;
}
