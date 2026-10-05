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

public sealed record CorePaymentResult(
    CoreOutcome Status,
    DateTimeOffset ProcessedAtUtc,
    string? CoreReference = null,
    string? ReasonCode = null,
    int? InternalErrorCode = null,
    string? Description = null);

public sealed record IncomingIpsDecision(bool Accepted, DateTimeOffset DecidedAtUtc, string? ReasonCode, string? Description);

public sealed record IncomingProcessingRecorded(
    IncomingProcessingOperation Operation,
    CoreOutcome CoreStatus,
    CorePaymentResult? CurrentResult,
    IncomingIpsDecision? IpsDecision,
    IncomingFollowUp FollowUp,
    CorePaymentResult? ObservedResult) : DomainEvent;
