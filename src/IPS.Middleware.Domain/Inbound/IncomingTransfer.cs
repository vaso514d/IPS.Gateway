namespace IPS.Middleware.Domain.Inbound;

public enum IncomingTransferOperation
{
    SubmissionStarted,
    CoreOutcomeRecorded,
    ResubmissionRequired,
    ManualReviewRequired
}

public sealed record IncomingTransferRegistered(string ParticipantBic, string Kind, string Key) : DomainEvent;

public sealed record IncomingTransferRecorded(
    IncomingTransferOperation Operation,
    CoreOutcome CoreStatus,
    int Attempts,
    string? ReasonCode,
    string? Description) : DomainEvent;

// A transfer received from IPS that only the core system settles (a pacs.009 or a pacs.004 return), identified by
// receiving participant, message kind and the exact business key (EndToEndId or return id). IPS is told nothing but
// receipt, so the only outcome is what the core system says.
public sealed class IncomingTransfer : AggregateRoot
{
    private IncomingTransfer()
    {
    }

    private IncomingTransfer(Guid id, string participantBic, string kind, string key, DateTimeOffset at) : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(participantBic);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrEmpty(key);

        ParticipantBic = participantBic.Trim().ToUpperInvariant();
        Kind = kind;
        // Kept exactly as received: it is the external CBS idempotency and status reference.
        Key = key;
        RegisteredAtUtc = at.ToUniversalTime();

        Raise(new IncomingTransferRegistered(ParticipantBic, Kind, Key), RegisteredAtUtc);
    }

    public string ParticipantBic { get; private set; } = string.Empty;
    public string Kind { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public DateTimeOffset RegisteredAtUtc { get; private set; }
    public CoreOutcome CoreStatus { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? CoreProcessedAtUtc { get; private set; }
    public string? CoreReference { get; private set; }
    public string? CoreReasonCode { get; private set; }
    public string? CoreDescription { get; private set; }
    public string? ManualReviewReason { get; private set; }

    public bool IsFinal => CoreStatus is CoreOutcome.Accepted or CoreOutcome.Rejected || ManualReviewReason is not null;

    public static IncomingTransfer Register(Guid id, string participantBic, string kind, string key, DateTimeOffset at) =>
        new(id, participantBic, kind, key, at);

    // The marker is committed before the core call, so a crash afterwards is recovered by asking the core.
    public void BeginSubmission(DateTimeOffset at)
    {
        if (CoreStatus != CoreOutcome.NotSubmitted || IsFinal)
        {
            throw new InvalidOperationException("A transfer is submitted to the core system only from its initial state.");
        }

        CoreStatus = CoreOutcome.SubmissionStarted;
        Attempts = checked(Attempts + 1);
        Record(IncomingTransferOperation.SubmissionStarted, at);
    }

    // An answer to a status question; it counts as an attempt only when the transfer was already submitted.
    public void BeginQuery()
    {
        if (CoreStatus is not (CoreOutcome.SubmissionStarted or CoreOutcome.Unknown) || IsFinal)
        {
            throw new InvalidOperationException("The core system is queried only after a submission and before a final outcome.");
        }

        Attempts = checked(Attempts + 1);
    }

    public void RecordCoreResult(CorePaymentResult result, DateTimeOffset at)
    {
        if (CoreStatus == CoreOutcome.NotSubmitted || IsFinal)
        {
            throw new InvalidOperationException("A core outcome requires a submission and no earlier final outcome.");
        }

        if (result.Status is not (CoreOutcome.Unknown or CoreOutcome.Accepted or CoreOutcome.Rejected))
        {
            throw new InvalidOperationException("Only an unknown, accepted or rejected core outcome can be recorded.");
        }

        CoreStatus = result.Status;
        if (result.Status != CoreOutcome.Unknown)
        {
            CoreProcessedAtUtc = result.ProcessedAtUtc.ToUniversalTime();
            CoreReference = result.CoreReference;
            CoreReasonCode = result.ReasonCode;
            CoreDescription = result.Description;
        }

        Record(IncomingTransferOperation.CoreOutcomeRecorded, at, result.ReasonCode, result.Description);
    }

    // The core never received the transfer, so the same request is sent again under the same key.
    public void RequireResubmission(DateTimeOffset at)
    {
        if (CoreStatus is not (CoreOutcome.SubmissionStarted or CoreOutcome.Unknown) || IsFinal)
        {
            throw new InvalidOperationException("Only an unanswered submission can be repeated.");
        }

        CoreStatus = CoreOutcome.NotSubmitted;
        Record(IncomingTransferOperation.ResubmissionRequired, at);
    }

    public void RequireManualReview(string reason, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (IsFinal)
        {
            throw new InvalidOperationException("A transfer with a final outcome needs no review.");
        }

        ManualReviewReason = reason;
        Record(IncomingTransferOperation.ManualReviewRequired, at, description: reason);
    }

    private void Record(IncomingTransferOperation operation, DateTimeOffset at, string? reasonCode = null, string? description = null) =>
        Raise(new IncomingTransferRecorded(operation, CoreStatus, Attempts, reasonCode, description), at);
}
