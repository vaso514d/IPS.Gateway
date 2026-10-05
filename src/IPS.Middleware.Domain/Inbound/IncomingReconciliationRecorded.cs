namespace IPS.Middleware.Domain.Inbound;

public enum ReversalDelivery
{
    None,
    Started,
    Accepted,
    Unsuccessful,
    Uncertain
}

public sealed record IncomingReconciliationRecorded(
    CoreOutcome CoreStatus,
    IncomingIpsDecision IpsDecision,
    IncomingFollowUp FollowUp,
    ReversalDelivery Reversal,
    DateTimeOffset? ReversalObservedAtUtc,
    string? ManualReviewReason) : DomainEvent;
