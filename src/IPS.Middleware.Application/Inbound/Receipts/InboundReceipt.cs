namespace IPS.Middleware.Application.Inbound.Receipts;

public enum InboundProcessingStatus
{
    Pending,
    Processed,
    Held
}

public sealed class InboundReceipt
{
    public InboundReceipt(
        string participantBic,
        long? sequence,
        string messageType,
        string rawXml,
        bool possibleDuplicate,
        DateTimeOffset receivedAtUtc)
    {
        ParticipantBic = Required(participantBic, 11, nameof(participantBic)).ToUpperInvariant();
        Sequence = sequence;
        MessageType = Required(messageType, 35, nameof(messageType));
        RawXml = rawXml ?? throw new ArgumentNullException(nameof(rawXml));
        PossibleDuplicate = possibleDuplicate;
        ReceivedAtUtc = receivedAtUtc.ToUniversalTime();
    }

    public string ParticipantBic { get; }
    public long? Sequence { get; }
    public string MessageType { get; }
    public string RawXml { get; }
    public bool PossibleDuplicate { get; }
    public DateTimeOffset ReceivedAtUtc { get; }

    private static string Required(string value, int limit, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        var normalized = value.Trim();
        return normalized.Length <= limit ? normalized : throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record InboundRegistration(Guid JournalId, bool Created, InboundProcessingStatus Status);

public sealed record InboundClaim(Guid JournalId, Guid Token, DateTimeOffset ExpiresAtUtc);

public sealed record OwnedInboundReceipt(Guid JournalId, string ParticipantBic, DateTimeOffset ReceivedAtUtc);

public sealed record StoredInboundReceipt(
    Guid JournalId,
    InboundReceipt Receipt,
    InboundProcessingStatus Status,
    string? HoldReason,
    DateTimeOffset? NextActionAtUtc,
    long DuplicateCount,
    DateTimeOffset? LastDuplicateAtUtc);
