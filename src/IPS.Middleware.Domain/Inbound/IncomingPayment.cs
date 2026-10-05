namespace IPS.Middleware.Domain.Inbound;

// A payment received from IPS, identified by receiving participant and exact EndToEndId.
public sealed class IncomingPayment : AggregateRoot
{
    private const string NoFinalResult = "Core system did not return a final payment result within the reply window.";
    private const string MissingFinalResultReason = "MS03";

    private IncomingPayment()
    {
    }

    private IncomingPayment(Guid id, string participantBic, string endToEndId, DateTimeOffset at) : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(participantBic);
        ArgumentException.ThrowIfNullOrEmpty(endToEndId);

        ParticipantBic = participantBic.Trim().ToUpperInvariant();
        // Kept exactly as received: it is the external CBS idempotency and status reference.
        EndToEndId = endToEndId;
        RegisteredAtUtc = at.ToUniversalTime();

        Raise(new IncomingPaymentRegistered(ParticipantBic, EndToEndId), RegisteredAtUtc);
    }

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
    public ReversalDelivery Reversal { get; private set; }
    public DateTimeOffset? ReversalObservedAtUtc { get; private set; }
    public string? ManualReviewReason { get; private set; }

    public bool HasFinalCoreOutcome => CoreStatus is CoreOutcome.Accepted or CoreOutcome.Rejected;

    public CorePaymentResult? CoreResult => CoreProcessedAtUtc is { } processedAt
        ? new(CoreStatus, processedAt, CoreReference, CoreReasonCode, CoreInternalErrorCode, CoreDescription)
        : null;

    public IncomingIpsDecision? IpsDecision => IpsAccepted is { } accepted
        ? new(accepted, IpsDecidedAtUtc!.Value, IpsReasonCode, IpsDescription)
        : null;

    public static IncomingPayment Register(Guid id, string participantBic, string endToEndId, DateTimeOffset at) =>
        new(id, participantBic, endToEndId, at);

    public void BeginSubmission(DateTimeOffset at)
    {
        if (CoreStatus != CoreOutcome.NotSubmitted || IpsAccepted is not null)
        {
            throw new InvalidOperationException("An incoming payment can be submitted only once, before its IPS decision.");
        }

        CoreStatus = CoreOutcome.SubmissionStarted;
        RecordProcessing(IncomingProcessingOperation.SubmissionStarted, at);
    }

    public void RecordCoreResult(CorePaymentResult result, DateTimeOffset observedAt)
    {
        var supported = result.Status is CoreOutcome.Unknown or CoreOutcome.Accepted or CoreOutcome.Rejected;
        if (!supported || CoreStatus == CoreOutcome.NotSubmitted)
        {
            throw new InvalidOperationException("A CBS outcome requires a submission marker and a supported outcome.");
        }

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
        if (IpsAccepted == false)
        {
            FollowUp = RequiredFollowUp();
        }

        RecordProcessing(IncomingProcessingOperation.CoreOutcomeRecorded, observedAt, result);
    }

    // Only a final CBS outcome inside the reply window decides the reply; anything else is RJCT/MS03.
    public void DecideIps(bool withinReplyWindow, DateTimeOffset at)
    {
        if (IpsAccepted is not null)
        {
            return;
        }

        var decidedByCore = withinReplyWindow && HasFinalCoreOutcome;
        IpsAccepted = decidedByCore && CoreStatus == CoreOutcome.Accepted;
        IpsDecidedAtUtc = at.ToUniversalTime();
        IpsReasonCode = IpsAccepted.Value ? null : RejectionReasonCode(decidedByCore);
        IpsDescription = decidedByCore ? CoreDescription : NoFinalResult;
        FollowUp = RequiredFollowUp();

        RecordProcessing(IncomingProcessingOperation.IpsDecisionRecorded, at);
    }

    public void BeginReversal(DateTimeOffset at)
    {
        var reversible = IpsAccepted == false
            && CoreStatus == CoreOutcome.Accepted
            && FollowUp == IncomingFollowUp.ReversalRequired
            && Reversal == ReversalDelivery.None;
        if (!reversible)
        {
            throw new InvalidOperationException("Only an unreversed credit rejected by IPS can start reversal.");
        }

        Reversal = ReversalDelivery.Started;
        RecordFollowUp(at);
    }

    public void RecordReversalDelivery(ReversalDelivery delivery, DateTimeOffset at)
    {
        var observable = delivery is ReversalDelivery.Accepted or ReversalDelivery.Unsuccessful or ReversalDelivery.Uncertain;
        if (Reversal != ReversalDelivery.Started || !observable)
        {
            throw new InvalidOperationException("A marked reversal accepts one delivery observation, never a completion inference.");
        }

        Reversal = delivery;
        ReversalObservedAtUtc = at.ToUniversalTime();

        var reason = delivery == ReversalDelivery.Accepted
            ? "CBS accepted the reversal request; completion requires authoritative evidence."
            : "Reversal delivery is unsuccessful or uncertain; do not automatically repeat it.";
        RequireManualReview(reason, at);
    }

    public void RequireManualReview(string reason, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (IpsAccepted != false || FollowUp == IncomingFollowUp.None)
        {
            throw new InvalidOperationException("Manual review requires an unresolved rejected incoming payment.");
        }

        FollowUp = IncomingFollowUp.ManualReviewRequired;
        ManualReviewReason = reason;
        RecordFollowUp(at);
    }

    // A final outcome keeps its original time and details; a contradicting final report requires manual review.
    private void ObserveAgain(CorePaymentResult result, DateTimeOffset observedAt)
    {
        var conflict = result.Status is CoreOutcome.Accepted or CoreOutcome.Rejected && result.Status != CoreStatus;
        if (conflict)
        {
            FollowUp = IncomingFollowUp.ManualReviewRequired;
        }

        var operation = conflict ? IncomingProcessingOperation.OutcomeConflictObserved : IncomingProcessingOperation.OutcomeObserved;
        RecordProcessing(operation, observedAt, result);
    }

    private string RejectionReasonCode(bool decidedByCore) =>
        decidedByCore && !string.IsNullOrWhiteSpace(CoreReasonCode) ? CoreReasonCode : MissingFinalResultReason;

    private IncomingFollowUp RequiredFollowUp() => this switch
    {
        { FollowUp: IncomingFollowUp.ManualReviewRequired } => IncomingFollowUp.ManualReviewRequired,
        { IpsAccepted: true } => IncomingFollowUp.None,
        { CoreStatus: CoreOutcome.Accepted } => IncomingFollowUp.ReversalRequired,
        { CoreStatus: CoreOutcome.SubmissionStarted or CoreOutcome.Unknown } => IncomingFollowUp.ReconciliationRequired,
        _ => IncomingFollowUp.None
    };

    private void RecordProcessing(IncomingProcessingOperation operation, DateTimeOffset at, CorePaymentResult? observed = null)
    {
        Raise(new IncomingProcessingRecorded(operation, CoreStatus, CoreResult, IpsDecision, FollowUp, observed), at);
    }

    private void RecordFollowUp(DateTimeOffset at)
    {
        var recorded = new IncomingReconciliationRecorded(
            CoreStatus,
            IpsDecision!,
            FollowUp,
            Reversal,
            ReversalObservedAtUtc,
            ManualReviewReason);
        Raise(recorded, at);
    }
}

public sealed record IncomingPaymentRegistered(string ParticipantBic, string EndToEndId) : DomainEvent;
