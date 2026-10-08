using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Persistence.Inbound;

internal sealed class IncomingReplyRow
{
    public Guid JournalId { get; set; }
    public string EnvelopeJson { get; set; } = string.Empty;
    public string? UnsignedXml { get; set; }
    public string? MessageXml { get; set; }
    public SubmissionMessageKind? MessageKind { get; set; }
    public IncomingReplyStatus Status { get; set; }
    public string? ReviewReason { get; set; }
}
internal sealed class IncomingReplyAttemptRow
{
    public Guid Id { get; set; }
    public Guid JournalId { get; set; }
    public int Number { get; set; }
    public Guid OwnerToken { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public string? CompletionJson { get; set; }
    public bool Consumed { get; set; }
    public IncomingReplyAttempt Snapshot() => new(Id, Number, OwnerToken, StartedAtUtc,
        CompletionJson is null ? null : IncomingPaymentJson.Read<ReplyAttemptCompletion>(CompletionJson), Consumed);
}
