namespace IPS.Middleware.Domain;

public abstract class AggregateRoot
{
    private readonly List<DomainEvent> _pendingEvents = [];

    protected AggregateRoot()
    {
    }

    protected AggregateRoot(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An aggregate identity is required.", nameof(id));
        }

        Id = id;
    }

    public Guid Id { get; private set; }
    public int EventSequence { get; private set; }
    public IReadOnlyList<DomainEvent> PendingEvents => _pendingEvents.AsReadOnly();

    // Only events included in a successfully committed transaction are acknowledged.
    public void AcknowledgeCommittedEvents(IReadOnlyCollection<Guid> eventIds)
    {
        _pendingEvents.RemoveAll(pending => eventIds.Contains(pending.EventId));
    }

    protected void Raise(DomainEvent occurrence, DateTimeOffset occurredAt)
    {
        EventSequence = checked(EventSequence + 1);
        _pendingEvents.Add(occurrence with
        {
            EventId = Guid.NewGuid(),
            AggregateId = Id,
            Sequence = EventSequence,
            OccurredAtUtc = occurredAt.ToUniversalTime()
        });
    }
}

public abstract record DomainEvent
{
    public Guid EventId { get; init; }
    public Guid AggregateId { get; init; }
    public int Sequence { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
}
