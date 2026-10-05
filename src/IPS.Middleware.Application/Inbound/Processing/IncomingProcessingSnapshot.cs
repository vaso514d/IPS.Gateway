using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Processing;
/// <summary>Frozen at registration from the originating receipt; duplicates never move it.</summary>
public sealed class IncomingProcessingContext
{
    public IncomingProcessingContext(Guid journalId, DateTimeOffset receivedAtUtc, DateTimeOffset deadlineUtc, IncomingPacs008Reference original)
    {
        JournalId = journalId;
        ReceivedAtUtc = receivedAtUtc;
        DeadlineUtc = deadlineUtc;
        Original = original;
    }

    public Guid JournalId { get; init; }
    public DateTimeOffset ReceivedAtUtc { get; init; }
    public DateTimeOffset DeadlineUtc { get; init; }
    public IncomingPacs008Reference Original { get; init; }
}

public sealed class IncomingProcessingSnapshot
{
    public IncomingProcessingSnapshot(
        IncomingPayment payment,
        Pacs008Request request,
        IncomingProcessingContext context,
        IReadOnlyList<IncomingCoreCall> calls,
        DateTimeOffset? followUpAtUtc,
        DateTimeOffset? reconciliationDeadlineUtc)
    {
        Payment = payment;
        Request = request;
        Context = context;
        Calls = calls;
        FollowUpAtUtc = followUpAtUtc;
        ReconciliationDeadlineUtc = reconciliationDeadlineUtc;
    }

    public IncomingPayment Payment { get; init; }
    public Pacs008Request Request { get; init; }
    public IncomingProcessingContext Context { get; init; }
    public IReadOnlyList<IncomingCoreCall> Calls { get; init; }
    public DateTimeOffset? FollowUpAtUtc { get; init; }
    public DateTimeOffset? ReconciliationDeadlineUtc { get; init; }
}

public sealed class IncomingProcessingResult
{
    public IncomingProcessingResult(CoreOutcome coreStatus, IncomingIpsDecision? ipsDecision, IncomingFollowUp followUp)
    {
        CoreStatus = coreStatus;
        IpsDecision = ipsDecision;
        FollowUp = followUp;
    }

    public CoreOutcome CoreStatus { get; init; }
    public IncomingIpsDecision? IpsDecision { get; init; }
    public IncomingFollowUp FollowUp { get; init; }
}
