using IPS.Middleware.Application.Inbound;

namespace IPS.Middleware.Infrastructure.Persistence.Inbound;

internal sealed class InboundJournalEntry
{
    public Guid Id { get; set; }
    public string ParticipantBic { get; set; } = string.Empty;
    public long? Sequence { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string RawXml { get; set; } = string.Empty;
    public bool PossibleDuplicate { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public InboundProcessingStatus Status { get; set; }
    public string? HoldReason { get; set; }
    public DateTimeOffset? NextActionAtUtc { get; set; }
    public Guid? ClaimToken { get; set; }
    public DateTimeOffset? ClaimExpiresAtUtc { get; set; }
    public long DuplicateCount { get; set; }
    public DateTimeOffset? LastDuplicateAtUtc { get; set; }
    public byte[] Version { get; set; } = [];

    public StoredInboundReceipt Snapshot() => new(Id,
        new(ParticipantBic, Sequence, MessageType, RawXml, PossibleDuplicate, ReceivedAtUtc),
        Status, HoldReason, NextActionAtUtc, DuplicateCount, LastDuplicateAtUtc);
}
