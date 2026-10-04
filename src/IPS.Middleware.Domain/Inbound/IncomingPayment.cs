namespace IPS.Middleware.Domain.Inbound;

/// <summary>A payment received from IPS, identified by receiving participant and exact EndToEndId.</summary>
public sealed class IncomingPayment : AggregateRoot
{
    private IncomingPayment() { }

    private IncomingPayment(Guid id, string participantBic, string endToEndId, DateTimeOffset at) : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(participantBic);
        ArgumentException.ThrowIfNullOrEmpty(endToEndId);
        ParticipantBic = participantBic.Trim().ToUpperInvariant();
        // Kept exactly as received: it is the external CBS idempotency and status reference.
        EndToEndId = endToEndId;
        RegisteredAtUtc = at.ToUniversalTime();
        Raise((eventId, sequence) => new IncomingPaymentRegistered(eventId, Id, sequence, RegisteredAtUtc, ParticipantBic, EndToEndId));
    }

    public static IncomingPayment Register(Guid id, string participantBic, string endToEndId, DateTimeOffset at) =>
        new(id, participantBic, endToEndId, at);

    public string ParticipantBic { get; private set; } = string.Empty;
    public string EndToEndId { get; private set; } = string.Empty;
    public DateTimeOffset RegisteredAtUtc { get; private set; }
}

public sealed record IncomingPaymentRegistered(
    Guid EventId, Guid AggregateId, int Sequence, DateTimeOffset OccurredAtUtc,
    string ParticipantBic, string EndToEndId)
    : DomainEvent(EventId, AggregateId, Sequence, OccurredAtUtc);
