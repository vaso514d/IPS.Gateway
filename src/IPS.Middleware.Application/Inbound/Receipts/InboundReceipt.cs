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

public sealed class InboundRegistration
{
    public InboundRegistration(Guid journalId, bool created, InboundProcessingStatus status)
    {
        JournalId = journalId;
        Created = created;
        Status = status;
    }

    public Guid JournalId { get; init; }
    public bool Created { get; init; }
    public InboundProcessingStatus Status { get; init; }
}

public sealed class InboundClaim
{
    [System.Text.Json.Serialization.JsonConstructor]
    public InboundClaim(Guid journalId, Guid token, DateTimeOffset expiresAtUtc)
    {
        JournalId = journalId;
        Token = token;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid JournalId { get; init; }
    public Guid Token { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }

    public InboundClaim(InboundClaim original)
    {
        JournalId = original.JournalId;
        Token = original.Token;
        ExpiresAtUtc = original.ExpiresAtUtc;
    }
}

public sealed class OwnedInboundReceipt
{
    public OwnedInboundReceipt(Guid journalId, string participantBic, DateTimeOffset receivedAtUtc)
    {
        JournalId = journalId;
        ParticipantBic = participantBic;
        ReceivedAtUtc = receivedAtUtc;
    }

    public Guid JournalId { get; init; }
    public string ParticipantBic { get; init; }
    public DateTimeOffset ReceivedAtUtc { get; init; }
}

public sealed class StoredInboundReceipt
{
    public StoredInboundReceipt(
        Guid journalId,
        InboundReceipt receipt,
        InboundProcessingStatus status,
        string? holdReason,
        DateTimeOffset? nextActionAtUtc,
        long duplicateCount,
        DateTimeOffset? lastDuplicateAtUtc)
    {
        JournalId = journalId;
        Receipt = receipt;
        Status = status;
        HoldReason = holdReason;
        NextActionAtUtc = nextActionAtUtc;
        DuplicateCount = duplicateCount;
        LastDuplicateAtUtc = lastDuplicateAtUtc;
    }

    public Guid JournalId { get; init; }
    public InboundReceipt Receipt { get; init; }
    public InboundProcessingStatus Status { get; init; }
    public string? HoldReason { get; init; }
    public DateTimeOffset? NextActionAtUtc { get; init; }
    public long DuplicateCount { get; init; }
    public DateTimeOffset? LastDuplicateAtUtc { get; init; }
}
