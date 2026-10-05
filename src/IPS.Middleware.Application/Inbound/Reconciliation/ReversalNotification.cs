namespace IPS.Middleware.Application.Inbound.Reconciliation;

public sealed class ReversalNotification : IEquatable<ReversalNotification>
{
    [System.Text.Json.Serialization.JsonConstructor]
    public ReversalNotification(
        Guid paymentId,
        string participantBic,
        string endToEndId,
        DateTimeOffset statusAtUtc,
        string? reasonCode,
        string? description,
        string groupMessageId)
    {
        PaymentId = paymentId;
        ParticipantBic = participantBic;
        EndToEndId = endToEndId;
        StatusAtUtc = statusAtUtc;
        ReasonCode = reasonCode;
        Description = description;
        GroupMessageId = groupMessageId;
    }

    public Guid PaymentId { get; init; }
    public string ParticipantBic { get; init; }
    public string EndToEndId { get; init; }
    public DateTimeOffset StatusAtUtc { get; init; }
    public string? ReasonCode { get; init; }
    public string? Description { get; init; }
    public string GroupMessageId { get; init; }

    public ReversalNotification(ReversalNotification original)
    {
        PaymentId = original.PaymentId;
        ParticipantBic = original.ParticipantBic;
        EndToEndId = original.EndToEndId;
        StatusAtUtc = original.StatusAtUtc;
        ReasonCode = original.ReasonCode;
        Description = original.Description;
        GroupMessageId = original.GroupMessageId;
    }

    public bool Equals(ReversalNotification? other) => other is not null &&
            Equals(PaymentId, other.PaymentId) &&
            Equals(ParticipantBic, other.ParticipantBic) &&
            Equals(EndToEndId, other.EndToEndId) &&
            Equals(StatusAtUtc, other.StatusAtUtc) &&
            Equals(ReasonCode, other.ReasonCode) &&
            Equals(Description, other.Description) &&
            Equals(GroupMessageId, other.GroupMessageId);
    public override bool Equals(object? obj) => obj is ReversalNotification other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PaymentId);
        hash.Add(ParticipantBic);
        hash.Add(EndToEndId);
        hash.Add(StatusAtUtc);
        hash.Add(ReasonCode);
        hash.Add(Description);
        hash.Add(GroupMessageId);
        return hash.ToHashCode();
    }

    public static bool operator ==(ReversalNotification? left, ReversalNotification? right) => Equals(left, right);
    public static bool operator !=(ReversalNotification? left, ReversalNotification? right) => !Equals(left, right);
}
