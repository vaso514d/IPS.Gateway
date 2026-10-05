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

public sealed class IncomingReplyEnvelope
{
    public IncomingReplyEnvelope(
        string participantBic,
        IncomingPacs008Reference original,
        IncomingReplyDecision decision,
        IncomingReplyContext context,
        Pacs008ProtocolProfile profile,
        int maxAttempts)
    {
        ParticipantBic = participantBic;
        Original = original;
        Decision = decision;
        Context = context;
        Profile = profile;
        MaxAttempts = maxAttempts;
    }

    public string ParticipantBic { get; init; }
    public IncomingPacs008Reference Original { get; init; }
    public IncomingReplyDecision Decision { get; init; }
    public IncomingReplyContext Context { get; init; }
    public Pacs008ProtocolProfile Profile { get; init; }
    public int MaxAttempts { get; init; }
}

public sealed class ReplyAttemptCompletion
{
    public ReplyAttemptCompletion(IpsSubmissionResponse? response, string? failure, DateTimeOffset observedAtUtc)
    {
        Response = response;
        Failure = failure;
        ObservedAtUtc = observedAtUtc;
    }

    public IpsSubmissionResponse? Response { get; init; }
    public string? Failure { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed class IncomingReplyAttempt
{
    [System.Text.Json.Serialization.JsonConstructor]
    public IncomingReplyAttempt(
        Guid id,
        int number,
        Guid ownerToken,
        DateTimeOffset startedAtUtc,
        ReplyAttemptCompletion? completion,
        bool consumed)
    {
        Id = id;
        Number = number;
        OwnerToken = ownerToken;
        StartedAtUtc = startedAtUtc;
        Completion = completion;
        Consumed = consumed;
    }

    public Guid Id { get; init; }
    public int Number { get; init; }
    public Guid OwnerToken { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public ReplyAttemptCompletion? Completion { get; init; }
    public bool Consumed { get; init; }

    public IncomingReplyAttempt(IncomingReplyAttempt original)
    {
        Id = original.Id;
        Number = original.Number;
        OwnerToken = original.OwnerToken;
        StartedAtUtc = original.StartedAtUtc;
        Completion = original.Completion;
        Consumed = original.Consumed;
    }
}

public sealed class IncomingReplySnapshot
{
    public IncomingReplySnapshot(
        Guid journalId,
        IncomingReplyEnvelope envelope,
        string? unsignedXml,
        string? messageXml,
        SubmissionMessageKind? messageKind,
        IncomingReplyStatus status,
        string? reviewReason,
        IReadOnlyList<IncomingReplyAttempt> attempts)
    {
        JournalId = journalId;
        Envelope = envelope;
        UnsignedXml = unsignedXml;
        MessageXml = messageXml;
        MessageKind = messageKind;
        Status = status;
        ReviewReason = reviewReason;
        Attempts = attempts;
    }

    public Guid JournalId { get; init; }
    public IncomingReplyEnvelope Envelope { get; init; }
    public string? UnsignedXml { get; init; }
    public string? MessageXml { get; init; }
    public SubmissionMessageKind? MessageKind { get; init; }
    public IncomingReplyStatus Status { get; init; }
    public string? ReviewReason { get; init; }
    public IReadOnlyList<IncomingReplyAttempt> Attempts { get; init; }
}

public sealed class ReplyDeliveryResult
{
    public ReplyDeliveryResult(ReplyDeliveryOutcome outcome, string description)
    {
        Outcome = outcome;
        Description = description;
    }

    public ReplyDeliveryOutcome Outcome { get; init; }
    public string Description { get; init; }
}
