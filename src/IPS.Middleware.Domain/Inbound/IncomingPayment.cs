namespace IPS.Middleware.Domain.Inbound;

/// <summary>A payment received from IPS, identified by receiving participant and exact EndToEndId.</summary>
public sealed class IncomingPayment : AggregateRoot
{
    private const string NoFinalResult = "Core system did not return a final payment result within the reply window.";

    private IncomingPayment() { }

    private IncomingPayment(Guid id, string participantBic, string endToEndId, DateTimeOffset at) : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(participantBic);
        ArgumentException.ThrowIfNullOrEmpty(endToEndId);
        ParticipantBic = participantBic.Trim().ToUpperInvariant();
        // Kept exactly as received: it is the external CBS idempotency and status reference.
        EndToEndId = endToEndId;
        RegisteredAtUtc = at.ToUniversalTime();
        Raise((eventId, sequence) => new IncomingPaymentRegistered(eventId, Id, sequence, RegisteredAtUtc, ParticipantBic, EndToEndId));
    }

    public static IncomingPayment Register(Guid id, string participantBic, string endToEndId, DateTimeOffset at) =>
        new(id, participantBic, endToEndId, at);

    public string ParticipantBic { get; private set; } = string.Empty;
    public string EndToEndId { get; private set; } = string.Empty;
    public DateTimeOffset RegisteredAtUtc { get; private set; }
    public CoreOutcome CoreStatus { get; private set; }
    public DateTimeOffset? CoreProcessedAtUtc { get; private set; }
    public string? CoreReference { get; private set; }
    public string? CoreReasonCode { get; private set; }
    public int? CoreInternalErrorCode { get; private set; }
    public string? CoreDescription { get; private set; }
    public bool? IpsAccepted { get; private set; }
    public DateTimeOffset? IpsDecidedAtUtc { get; private set; }
    public string? IpsReasonCode { get; private set; }
    public string? IpsDescription { get; private set; }
    public IncomingFollowUp FollowUp { get; private set; }
    public bool HasFinalCoreOutcome => CoreStatus is CoreOutcome.Accepted or CoreOutcome.Rejected;
    public CorePaymentResult? CoreResult => CoreProcessedAtUtc is { } at
        ? new(CoreStatus, at, CoreReference, CoreReasonCode, CoreInternalErrorCode, CoreDescription) : null;
    public IncomingIpsDecision? IpsDecision => IpsAccepted is { } accepted
        ? new(accepted, IpsDecidedAtUtc!.Value, IpsReasonCode, IpsDescription) : null;

    public void BeginSubmission(DateTimeOffset at)
    {
        if (CoreStatus != CoreOutcome.NotSubmitted || IpsAccepted is not null)
            throw new InvalidOperationException("An incoming payment can be submitted only once, before its IPS decision.");
        CoreStatus = CoreOutcome.SubmissionStarted;
        Record(IncomingProcessingOperation.SubmissionStarted, at);
    }

    public void RecordCoreResult(CorePaymentResult result, DateTimeOffset observedAt)
    {
        if (result.Status is not (CoreOutcome.Unknown or CoreOutcome.Accepted or CoreOutcome.Rejected) || CoreStatus == CoreOutcome.NotSubmitted)
            throw new InvalidOperationException("A CBS outcome requires a submission marker and a supported outcome.");
        if (HasFinalCoreOutcome)
        {
            ObserveAgain(result, observedAt);
            return;
        }
        CoreStatus = result.Status;
        CoreProcessedAtUtc = result.ProcessedAtUtc.ToUniversalTime();
        CoreReference = result.CoreReference;
        CoreReasonCode = result.ReasonCode;
        CoreInternalErrorCode = result.InternalErrorCode;
        CoreDescription = result.Description;
        if (IpsAccepted == false) FollowUp = RequiredFollowUp();
        Record(IncomingProcessingOperation.CoreOutcomeRecorded, observedAt, result);
    }

    public void DecideIps(bool withinReplyWindow, DateTimeOffset at)
    {
        if (IpsAccepted is not null) return;
        // Only a final CBS outcome inside the reply window decides the reply; anything else is RJCT/MS03.
        var final = withinReplyWindow && HasFinalCoreOutcome;
        IpsAccepted = final && CoreStatus == CoreOutcome.Accepted;
        IpsDecidedAtUtc = at.ToUniversalTime();
        IpsReasonCode = IpsAccepted.Value ? null : final && !string.IsNullOrWhiteSpace(CoreReasonCode) ? CoreReasonCode : "MS03";
        IpsDescription = final ? CoreDescription : NoFinalResult;
        FollowUp = RequiredFollowUp();
        Record(IncomingProcessingOperation.IpsDecisionRecorded, at);
    }

    // A final outcome keeps its original time and details; a contradicting final report requires manual review.
    private void ObserveAgain(CorePaymentResult result, DateTimeOffset observedAt)
    {
        var conflict = result.Status is CoreOutcome.Accepted or CoreOutcome.Rejected && result.Status != CoreStatus;
        if (conflict) FollowUp = IncomingFollowUp.ManualReviewRequired;
        Record(conflict ? IncomingProcessingOperation.OutcomeConflictObserved : IncomingProcessingOperation.OutcomeObserved, observedAt, result);
    }

    private IncomingFollowUp RequiredFollowUp() => this switch
    {
        { FollowUp: IncomingFollowUp.ManualReviewRequired } => IncomingFollowUp.ManualReviewRequired,
        { IpsAccepted: true } => IncomingFollowUp.None,
        { CoreStatus: CoreOutcome.Accepted } => IncomingFollowUp.ReversalRequired,
        { CoreStatus: CoreOutcome.SubmissionStarted or CoreOutcome.Unknown } => IncomingFollowUp.ReconciliationRequired,
        _ => IncomingFollowUp.None
    };

    private void Record(IncomingProcessingOperation operation, DateTimeOffset at, CorePaymentResult? observed = null) =>
        Raise((id, sequence) => new IncomingProcessingRecorded(id, Id, sequence, at.ToUniversalTime(),
            operation, CoreStatus, CoreResult, IpsDecision, FollowUp, observed));
}

public sealed record IncomingPaymentRegistered(
    Guid EventId, Guid AggregateId, int Sequence, DateTimeOffset OccurredAtUtc,
    string ParticipantBic, string EndToEndId)
    : DomainEvent(EventId, AggregateId, Sequence, OccurredAtUtc);
