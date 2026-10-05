namespace IPS.Middleware.Domain.Inbound;

public enum ReversalDelivery
{
    None,
    Started,
    Accepted,
    Unsuccessful,
    Uncertain
}

public sealed class IncomingReconciliationRecorded : DomainEvent
{
    public IncomingReconciliationRecorded(
        Guid eventId,
        Guid aggregateId,
        int sequence,
        DateTimeOffset occurredAtUtc,
        CoreOutcome coreStatus,
        IncomingIpsDecision ipsDecision,
        IncomingFollowUp followUp,
        ReversalDelivery reversal,
        DateTimeOffset? reversalObservedAtUtc,
        string? manualReviewReason) : base(eventId, aggregateId, sequence, occurredAtUtc)
    {
        CoreStatus = coreStatus;
        IpsDecision = ipsDecision;
        FollowUp = followUp;
        Reversal = reversal;
        ReversalObservedAtUtc = reversalObservedAtUtc;
        ManualReviewReason = manualReviewReason;
    }

    public CoreOutcome CoreStatus { get; init; }
    public IncomingIpsDecision IpsDecision { get; init; }
    public IncomingFollowUp FollowUp { get; init; }
    public ReversalDelivery Reversal { get; init; }
    public DateTimeOffset? ReversalObservedAtUtc { get; init; }
    public string? ManualReviewReason { get; init; }
}
