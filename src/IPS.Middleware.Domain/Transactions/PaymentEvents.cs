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

public sealed record PaymentDetails
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
}

public sealed record PaymentOutcome(
    TransactionStatus Status, StatusSource Source, DateTimeOffset AtUtc, int Sequence, PaymentDetails Details);

public sealed record PaymentReceived(
    Guid EventId, Guid AggregateId, int Sequence, DateTimeOffset OccurredAtUtc,
    string MessageType, string ClientReference)
    : DomainEvent(EventId, AggregateId, Sequence, OccurredAtUtc);

public sealed record PaymentStateChanged(
    Guid EventId, Guid AggregateId, int Sequence, DateTimeOffset OccurredAtUtc,
    PaymentOperation Operation, TransactionStatus PreviousStatus, TransactionStatus Status,
    StatusSource Source, PaymentDetails Details)
    : DomainEvent(EventId, AggregateId, Sequence, OccurredAtUtc);

public sealed record PaymentProcessingObserved(
    Guid EventId, Guid AggregateId, int Sequence, DateTimeOffset OccurredAtUtc,
    ProcessingStep Step, TransactionStatus Status)
    : DomainEvent(EventId, AggregateId, Sequence, OccurredAtUtc);

public sealed record PaymentProcessingFailed(
    Guid EventId, Guid AggregateId, int Sequence, DateTimeOffset OccurredAtUtc,
    ProcessingStep Step, TransactionStatus Status, string Description)
    : DomainEvent(EventId, AggregateId, Sequence, OccurredAtUtc);

public sealed record PaymentOutcomeObserved(
    Guid EventId, Guid AggregateId, int Sequence, DateTimeOffset OccurredAtUtc,
    TransactionStatus CurrentStatus, TransactionStatus ReportedStatus, bool Conflicting,
    StatusSource Source, PaymentDetails Details)
    : DomainEvent(EventId, AggregateId, Sequence, OccurredAtUtc);

public sealed class PaymentTransitionException(TransactionStatus state, PaymentOperation operation)
    : InvalidOperationException($"Payment operation {operation} is not valid in state {state}.")
{
    public TransactionStatus State { get; } = state;
    public PaymentOperation Operation { get; } = operation;
}
