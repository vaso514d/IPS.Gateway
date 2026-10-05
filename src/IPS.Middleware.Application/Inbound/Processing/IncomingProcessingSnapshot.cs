using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Processing;

// Frozen at registration from the originating receipt; duplicates never move it.
public sealed record IncomingProcessingContext(
    Guid JournalId,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset DeadlineUtc,
    IncomingPacs008Reference Original);

public sealed record IncomingProcessingSnapshot(
    IncomingPayment Payment,
    Pacs008Request Request,
    IncomingProcessingContext Context,
    IReadOnlyList<IncomingCoreCall> Calls,
    DateTimeOffset? FollowUpAtUtc,
    DateTimeOffset? ReconciliationDeadlineUtc);

public sealed record IncomingProcessingResult(CoreOutcome CoreStatus, IncomingIpsDecision? IpsDecision, IncomingFollowUp FollowUp);
