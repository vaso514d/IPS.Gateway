namespace IPS.Middleware.Domain.Inbound;

public enum CoreOutcome
{
    NotSubmitted,
    SubmissionStarted,
    Unknown,
    Accepted,
    Rejected
}

public enum IncomingProcessingOperation
{
    SubmissionStarted,
    CoreOutcomeRecorded,
    IpsDecisionRecorded,
    OutcomeObserved,
    OutcomeConflictObserved
}

public enum IncomingFollowUp
{
    None,
    ReconciliationRequired,
    ReversalRequired,
    ManualReviewRequired
}

public sealed class CorePaymentResult : IEquatable<CorePaymentResult>
{
    [System.Text.Json.Serialization.JsonConstructor]
    public CorePaymentResult(
        CoreOutcome status,
        DateTimeOffset processedAtUtc,
        string? coreReference = null,
        string? reasonCode = null,
        int? internalErrorCode = null,
        string? description = null)
    {
        Status = status;
        ProcessedAtUtc = processedAtUtc;
        CoreReference = coreReference;
        ReasonCode = reasonCode;
        InternalErrorCode = internalErrorCode;
        Description = description;
    }

    public CoreOutcome Status { get; init; }
    public DateTimeOffset ProcessedAtUtc { get; init; }
    public string? CoreReference { get; init; }
    public string? ReasonCode { get; init; }
    public int? InternalErrorCode { get; init; }
    public string? Description { get; init; }

    public CorePaymentResult(CorePaymentResult original)
    {
        Status = original.Status;
        ProcessedAtUtc = original.ProcessedAtUtc;
        CoreReference = original.CoreReference;
        ReasonCode = original.ReasonCode;
        InternalErrorCode = original.InternalErrorCode;
        Description = original.Description;
    }

    public bool Equals(CorePaymentResult? other) => other is not null &&
            Equals(Status, other.Status) &&
            Equals(ProcessedAtUtc, other.ProcessedAtUtc) &&
            Equals(CoreReference, other.CoreReference) &&
            Equals(ReasonCode, other.ReasonCode) &&
            Equals(InternalErrorCode, other.InternalErrorCode) &&
            Equals(Description, other.Description);
    public override bool Equals(object? obj) => obj is CorePaymentResult other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Status);
        hash.Add(ProcessedAtUtc);
        hash.Add(CoreReference);
        hash.Add(ReasonCode);
        hash.Add(InternalErrorCode);
        hash.Add(Description);
        return hash.ToHashCode();
    }

    public static bool operator ==(CorePaymentResult? left, CorePaymentResult? right) => Equals(left, right);
    public static bool operator !=(CorePaymentResult? left, CorePaymentResult? right) => !Equals(left, right);
}

public sealed class IncomingIpsDecision : IEquatable<IncomingIpsDecision>
{
    public IncomingIpsDecision(bool accepted, DateTimeOffset decidedAtUtc, string? reasonCode, string? description)
    {
        Accepted = accepted;
        DecidedAtUtc = decidedAtUtc;
        ReasonCode = reasonCode;
        Description = description;
    }

    public bool Accepted { get; init; }
    public DateTimeOffset DecidedAtUtc { get; init; }
    public string? ReasonCode { get; init; }
    public string? Description { get; init; }

    public bool Equals(IncomingIpsDecision? other) => other is not null &&
            Equals(Accepted, other.Accepted) &&
            Equals(DecidedAtUtc, other.DecidedAtUtc) &&
            Equals(ReasonCode, other.ReasonCode) &&
            Equals(Description, other.Description);
    public override bool Equals(object? obj) => obj is IncomingIpsDecision other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Accepted);
        hash.Add(DecidedAtUtc);
        hash.Add(ReasonCode);
        hash.Add(Description);
        return hash.ToHashCode();
    }

    public static bool operator ==(IncomingIpsDecision? left, IncomingIpsDecision? right) => Equals(left, right);
    public static bool operator !=(IncomingIpsDecision? left, IncomingIpsDecision? right) => !Equals(left, right);
}

public sealed class IncomingProcessingRecorded : DomainEvent
{
    public IncomingProcessingRecorded(
        Guid eventId,
        Guid aggregateId,
        int sequence,
        DateTimeOffset occurredAtUtc,
        IncomingProcessingOperation operation,
        CoreOutcome coreStatus,
        CorePaymentResult? currentResult,
        IncomingIpsDecision? ipsDecision,
        IncomingFollowUp followUp,
        CorePaymentResult? observedResult) : base(eventId, aggregateId, sequence, occurredAtUtc)
    {
        Operation = operation;
        CoreStatus = coreStatus;
        CurrentResult = currentResult;
        IpsDecision = ipsDecision;
        FollowUp = followUp;
        ObservedResult = observedResult;
    }

    public IncomingProcessingOperation Operation { get; init; }
    public CoreOutcome CoreStatus { get; init; }
    public CorePaymentResult? CurrentResult { get; init; }
    public IncomingIpsDecision? IpsDecision { get; init; }
    public IncomingFollowUp FollowUp { get; init; }
    public CorePaymentResult? ObservedResult { get; init; }
}
