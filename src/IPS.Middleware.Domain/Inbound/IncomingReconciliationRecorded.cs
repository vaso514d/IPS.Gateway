namespace IPS.Middleware.Domain.Inbound;

public enum ReversalDelivery { None, Started, Accepted, Unsuccessful, Uncertain }
public sealed record IncomingReconciliationRecorded(Guid EventId, Guid AggregateId, int Sequence, DateTimeOffset OccurredAtUtc,
    CoreOutcome CoreStatus, IncomingIpsDecision IpsDecision, IncomingFollowUp FollowUp, ReversalDelivery Reversal,
    DateTimeOffset? ReversalObservedAtUtc, string? ManualReviewReason) : DomainEvent(EventId, AggregateId, Sequence, OccurredAtUtc);
