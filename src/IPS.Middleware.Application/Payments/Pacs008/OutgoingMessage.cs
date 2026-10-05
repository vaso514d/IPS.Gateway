namespace IPS.Middleware.Application.Payments.Pacs008;

public enum MessageJournalStatus
{
    ReadyToSend,
    SendStarted,
    Received,
    Processed,
    Failed
}

public enum OutgoingMessageDirection
{
    Outbound,
    Response
}

/// <summary>Committed technical evidence, separate from the payment's business outcome.</summary>
public sealed class OutgoingMessage
{
    public OutgoingMessage(
        Guid id,
        Guid paymentId,
        OutgoingMessageDirection direction,
        string? messageDefinition,
        string content,
        DateTimeOffset createdAtUtc,
        Guid? originatingMessageId,
        MessageJournalStatus status,
        SubmissionMessageKind? disposition,
        SubmissionMarker? submission,
        IpsSubmissionResponse? response,
        DateTimeOffset? processedAtUtc,
        string? failure,
        Guid? investigationId = null)
    {
        Id = id;
        PaymentId = paymentId;
        Direction = direction;
        MessageDefinition = messageDefinition;
        Content = content;
        CreatedAtUtc = createdAtUtc;
        OriginatingMessageId = originatingMessageId;
        Status = status;
        Disposition = disposition;
        Submission = submission;
        Response = response;
        ProcessedAtUtc = processedAtUtc;
        Failure = failure;
        InvestigationId = investigationId;
    }

    public Guid Id { get; init; }
    public Guid PaymentId { get; init; }
    public OutgoingMessageDirection Direction { get; init; }
    public string? MessageDefinition { get; init; }
    public string Content { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public Guid? OriginatingMessageId { get; init; }
    public MessageJournalStatus Status { get; init; }
    public SubmissionMessageKind? Disposition { get; init; }
    public SubmissionMarker? Submission { get; init; }
    public IpsSubmissionResponse? Response { get; init; }
    public DateTimeOffset? ProcessedAtUtc { get; init; }
    public string? Failure { get; init; }
    public Guid? InvestigationId { get; init; }
}
