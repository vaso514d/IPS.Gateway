using System.Collections.ObjectModel;

namespace IPS.Middleware.Domain;

public abstract class AggregateRoot
{
    private readonly List<DomainEvent> _events = [];
    private readonly ReadOnlyCollection<DomainEvent> _eventView;
    protected AggregateRoot() => _eventView = _events.AsReadOnly();
    protected AggregateRoot(Guid id) : this()
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An aggregate identity is required.", nameof(id));
        }

        Id = id;
    }

    public Guid Id { get; private set; }
    public int EventSequence { get; private set; }
    public IReadOnlyList<DomainEvent> PendingEvents => _eventView;

    protected void Raise(Func<Guid, int, DomainEvent> create)
    {
        var sequence = checked(EventSequence + 1);
        var occurrence = create(Guid.NewGuid(), sequence);
        _events.Add(occurrence);
        EventSequence = sequence;
    }

    /// <summary>Acknowledge only the events included in a successfully committed transaction.</summary>
    public void AcknowledgeCommittedEvents(IReadOnlyCollection<Guid> eventIds) => _events.RemoveAll(occurrence => eventIds.Contains(occurrence.EventId));
}

public abstract class DomainEvent
{
    protected DomainEvent(Guid eventId, Guid aggregateId, int sequence, DateTimeOffset occurredAtUtc)
    {
        EventId = eventId;
        AggregateId = aggregateId;
        Sequence = sequence;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid EventId { get; init; }
    public Guid AggregateId { get; init; }
    public int Sequence { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
}
