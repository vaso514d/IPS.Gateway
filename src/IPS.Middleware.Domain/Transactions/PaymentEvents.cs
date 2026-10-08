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
    private const int MaxDescriptionLength = 2000;

    public PaymentDetails(string? reasonCode = null, int? ipsInternalCode = null, string? description = null)
    {
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? null : reasonCode.Trim().ToUpperInvariant();
        IpsInternalCode = ipsInternalCode;
        Description = Normalize(description);
    }

    public string? ReasonCode { get; }
    public int? IpsInternalCode { get; }
    public string? Description { get; }

    private static string? Normalize(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var trimmed = description.Trim();
        return trimmed.Length > MaxDescriptionLength ? trimmed[..MaxDescriptionLength] : trimmed;
    }
}

public sealed record PaymentOutcome(
    TransactionStatus Status,
    StatusSource Source,
    DateTimeOffset AtUtc,
    int Sequence,
    PaymentDetails Details);

public sealed record PaymentReceived(string MessageType, string ClientReference) : DomainEvent;

public sealed record PaymentStateChanged(
    PaymentOperation Operation,
    TransactionStatus PreviousStatus,
    TransactionStatus Status,
    StatusSource Source,
    PaymentDetails Details) : DomainEvent;

public sealed record PaymentProcessingObserved(ProcessingStep Step, TransactionStatus Status) : DomainEvent;

public sealed record PaymentProcessingFailed(ProcessingStep Step, TransactionStatus Status, string Description) : DomainEvent;

public sealed record PaymentOutcomeObserved(
    TransactionStatus CurrentStatus,
    TransactionStatus ReportedStatus,
    bool Conflicting,
    StatusSource Source,
    PaymentDetails Details) : DomainEvent;

// The creditor bank refused our recall with a camt.029: its reason code and the camt.029's message and cancellation status ids.
public sealed record RecallRefused(string? ReasonCode, string MessageId, string CancellationStatusId) : DomainEvent;

public sealed class PaymentTransitionException(TransactionStatus state, PaymentOperation operation)
    : InvalidOperationException($"Payment operation {operation} is not valid in state {state}.")
{
    public TransactionStatus State { get; } = state;
    public PaymentOperation Operation { get; } = operation;
}
