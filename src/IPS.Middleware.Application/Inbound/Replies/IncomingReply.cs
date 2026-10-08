using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Inbound.Replies;

public enum IncomingReplyStatus
{
    Preparing,
    Ready,
    Delivered,
    ManualReview
}

public enum ReplyDeliveryOutcome
{
    Unresolved,
    Delivered,
    Conflict
}

public sealed record IncomingReplyEnvelope(
    string ParticipantBic,
    IncomingPacs008Reference Original,
    IncomingReplyDecision Decision,
    IncomingReplyContext Context,
    Pacs008ProtocolProfile Profile,
    int MaxAttempts);

public sealed record ReplyAttemptCompletion(IpsSubmissionResponse? Response, string? Failure, DateTimeOffset ObservedAtUtc);

public sealed record IncomingReplyAttempt(
    Guid Id,
    int Number,
    Guid OwnerToken,
    DateTimeOffset StartedAtUtc,
    ReplyAttemptCompletion? Completion,
    bool Consumed);

public sealed record IncomingReplySnapshot(
    Guid JournalId,
    IncomingReplyEnvelope Envelope,
    string? UnsignedXml,
    string? MessageXml,
    SubmissionMessageKind? MessageKind,
    IncomingReplyStatus Status,
    string? ReviewReason,
    IReadOnlyList<IncomingReplyAttempt> Attempts);

public sealed record ReplyDeliveryResult(ReplyDeliveryOutcome Outcome, string Description);
