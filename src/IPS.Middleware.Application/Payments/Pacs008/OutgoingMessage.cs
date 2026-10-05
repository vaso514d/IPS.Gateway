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

// Committed technical evidence, separate from the payment's business outcome.
public sealed record OutgoingMessage(
    Guid Id,
    Guid PaymentId,
    OutgoingMessageDirection Direction,
    string? MessageDefinition,
    string Content,
    DateTimeOffset CreatedAtUtc,
    Guid? OriginatingMessageId,
    MessageJournalStatus Status,
    SubmissionMessageKind? Disposition,
    SubmissionMarker? Submission,
    IpsSubmissionResponse? Response,
    DateTimeOffset? ProcessedAtUtc,
    string? Failure,
    Guid? InvestigationId = null);
