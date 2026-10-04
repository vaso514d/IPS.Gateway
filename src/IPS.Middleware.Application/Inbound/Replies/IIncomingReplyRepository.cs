using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Receipts;

namespace IPS.Middleware.Application.Inbound.Replies;

public interface IIncomingReplyRepository
{
    Task<IncomingReplySnapshot?> ReadAsync(Guid journalId, CancellationToken cancellationToken);
    Task<IncomingReplyDecision?> ReadDecisionAsync(Guid journalId, CancellationToken cancellationToken);
    Task StageEnvelopeAsync(InboundClaim claim, IncomingReplyEnvelope envelope, DateTimeOffset now, CancellationToken cancellationToken);
    Task StageUnsignedAsync(InboundClaim claim, string xml, DateTimeOffset now, CancellationToken cancellationToken);
    Task StageMessageAsync(InboundClaim claim, SignedMessage message, DateTimeOffset now, CancellationToken cancellationToken);
    Task<IncomingReplyAttempt> StageAttemptAsync(InboundClaim claim, DateTimeOffset now, CancellationToken cancellationToken);
    Task StageCompletionAsync(InboundClaim claim, Guid attemptId, ReplyAttemptCompletion completion, DateTimeOffset now, CancellationToken cancellationToken);
    Task StageConsumptionAsync(InboundClaim claim, Guid attemptId, DateTimeOffset now, CancellationToken cancellationToken);
    Task StageOutcomeAsync(InboundClaim claim, IncomingReplyStatus status, string? reason, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> IsOwnerAsync(InboundClaim claim, DateTimeOffset now, CancellationToken cancellationToken);
}
