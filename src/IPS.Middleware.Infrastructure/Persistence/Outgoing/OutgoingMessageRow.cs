using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Persistence.Outgoing;

internal sealed class OutgoingMessageRow
{
    public Guid Id { get; set; }
    public Guid? InvestigationId { get; set; }
    public Guid? ResendId { get; set; }
    public Guid PaymentId { get; set; }
    public OutgoingMessageDirection Direction { get; set; }
    public string? MessageDefinition { get; set; }
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? OriginatingMessageId { get; set; }
    public MessageJournalStatus Status { get; set; }
    public SubmissionMessageKind? Disposition { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public Guid? SubmissionOwner { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? HeadersJson { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public string? Failure { get; set; }

    internal OutgoingMessage Snapshot() => new(Id, PaymentId, Direction, MessageDefinition, Content, CreatedAtUtc,
        OriginatingMessageId, Status, Disposition,
        StartedAtUtc is { } started ? new(started, SubmissionOwner!.Value, Disposition!.Value) : null,
        HttpStatusCode is { } status ? new(status, Content, PaymentJson.Read<List<IpsResponseHeader>>(HeadersJson)!) : null,
        ProcessedAtUtc, Failure, InvestigationId, ResendId);
}
