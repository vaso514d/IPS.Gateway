using Stateless;

namespace IPS.Middleware.Domain.Transactions;

public sealed class OutgoingPayment : AggregateRoot
{
    private StateMachine<TransactionStatus, PaymentOperation>? _machine;

    private OutgoingPayment() { }

    private OutgoingPayment(Guid id, string messageType, string clientReference, DateTimeOffset at) : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientReference);
        MessageType = messageType.Trim();
        ClientReference = clientReference.Trim();
        CreatedAtUtc = at.ToUniversalTime();
        CurrentStatus = TransactionStatus.Received;
        CurrentSource = StatusSource.Gateway;
        CurrentStatusAtUtc = CreatedAtUtc;
        Raise((eventId, sequence) => new PaymentReceived(eventId, Id, sequence, CreatedAtUtc, MessageType, ClientReference));
        CurrentSequence = EventSequence;
    }

    public static OutgoingPayment Receive(Guid id, string messageType, string clientReference, DateTimeOffset at) =>
        new(id, messageType, clientReference, at);

    public string MessageType { get; private set; } = string.Empty;
    public string ClientReference { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public TransactionStatus CurrentStatus { get; private set; }
    public StatusSource CurrentSource { get; private set; }
    public DateTimeOffset CurrentStatusAtUtc { get; private set; }
    public int CurrentSequence { get; private set; }
    public string? CurrentReasonCode { get; private set; }
    public int? CurrentIpsInternalCode { get; private set; }
    public string? CurrentDescription { get; private set; }
    public PaymentOutcome Current => new(CurrentStatus, CurrentSource, CurrentStatusAtUtc, CurrentSequence,
        new(CurrentReasonCode, CurrentIpsInternalCode, CurrentDescription));
    public bool IsFinal => CurrentStatus is TransactionStatus.Accepted or TransactionStatus.Rejected
        or TransactionStatus.NotSent or TransactionStatus.ManuallyResolved;

    public void BeginSending(DateTimeOffset at) => Transition(PaymentOperation.BeginSending, StatusSource.Gateway, at);
    public void RecordAcceptance(StatusSource source, DateTimeOffset at, PaymentDetails? details = null) =>
        RecordOutcome(PaymentOperation.Accept, TransactionStatus.Accepted, source, at, details);
    public void RecordRejection(StatusSource source, DateTimeOffset at, PaymentDetails? details = null) =>
        RecordOutcome(PaymentOperation.Reject, TransactionStatus.Rejected, source, at, details);
    public void ScheduleConnectionRetry(DateTimeOffset at, PaymentDetails? details = null) =>
        Transition(PaymentOperation.RetryConnection, StatusSource.Gateway, at, details);
    public void RecordNotSent(DateTimeOffset at, PaymentDetails? details = null) =>
        RecordOutcome(PaymentOperation.NotSent, TransactionStatus.NotSent, StatusSource.Gateway, at, details);
    public void MarkOutcomeUnknown(StatusSource source, DateTimeOffset at, PaymentDetails? details = null) =>
        Transition(PaymentOperation.OutcomeUnknown, source, at, details);
    public void BeginInvestigation(DateTimeOffset at) => Transition(PaymentOperation.BeginInvestigation, StatusSource.Recovery, at);
    public void BeginResending(StatusSource source, DateTimeOffset at) => Transition(PaymentOperation.BeginResending, source, at);
    public void RequireManualReview(DateTimeOffset at, PaymentDetails? details = null) =>
        Transition(PaymentOperation.RequireManualReview, StatusSource.Recovery, at, details);
    public void ResolveManually(DateTimeOffset at, PaymentDetails? details = null) =>
        RecordOutcome(PaymentOperation.ResolveManually, TransactionStatus.ManuallyResolved, StatusSource.Operator, at, details);

    public void RecordStep(ProcessingStep step, DateTimeOffset at)
    {
        if (!Enum.IsDefined(step)) throw new ArgumentOutOfRangeException(nameof(step));
        Raise((eventId, sequence) => new PaymentProcessingObserved(eventId, Id, sequence, at.ToUniversalTime(), step, CurrentStatus));
    }

    /// <summary>Record a technical failure that leaves the current state unchanged and retryable.</summary>
    public void RecordProcessingFailure(ProcessingStep step, DateTimeOffset at, string description)
    {
        if (!Enum.IsDefined(step)) throw new ArgumentOutOfRangeException(nameof(step));
        var details = new PaymentDetails(description: description);
        if (details.Description is null) throw new ArgumentException("A failure description is required.", nameof(description));
        Raise((eventId, sequence) => new PaymentProcessingFailed(eventId, Id, sequence, at.ToUniversalTime(), step, CurrentStatus, details.Description));
    }

    private void RecordOutcome(
        PaymentOperation operation, TransactionStatus reported, StatusSource source, DateTimeOffset at, PaymentDetails? details)
    {
        if (!Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(source));
        // Operator resolution is an explicit correction; ordinary final replies are observations.
        if ((IsFinal && operation != PaymentOperation.ResolveManually) ||
            (CurrentStatus == TransactionStatus.ManuallyResolved && operation == PaymentOperation.ResolveManually))
        {
            Raise((eventId, sequence) => new PaymentOutcomeObserved(eventId, Id, sequence, at.ToUniversalTime(),
                CurrentStatus, reported, CurrentStatus != reported, source, details ?? new()));
            return;
        }
        Transition(operation, source, at, details);
    }

    private void Transition(PaymentOperation operation, StatusSource source, DateTimeOffset at, PaymentDetails? details = null)
    {
        if (!Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(source));
        var machine = _machine ??= ConfigureMachine();
        if (!machine.CanFire(operation)) throw new PaymentTransitionException(CurrentStatus, operation);
        var previous = CurrentStatus;
        var normalized = details ?? new();
        machine.Fire(operation);
        CurrentSource = source;
        CurrentStatusAtUtc = at.ToUniversalTime();
        CurrentReasonCode = normalized.ReasonCode;
        CurrentIpsInternalCode = normalized.IpsInternalCode;
        CurrentDescription = normalized.Description;
        Raise((eventId, sequence) => new PaymentStateChanged(eventId, Id, sequence, CurrentStatusAtUtc,
            operation, previous, CurrentStatus, source, normalized));
        CurrentSequence = EventSequence;
    }

    private StateMachine<TransactionStatus, PaymentOperation> ConfigureMachine()
    {
        var machine = new StateMachine<TransactionStatus, PaymentOperation>(() => CurrentStatus, status => CurrentStatus = status);
        machine.Configure(TransactionStatus.Received)
            .Permit(PaymentOperation.BeginSending, TransactionStatus.Sending)
            .Permit(PaymentOperation.Reject, TransactionStatus.Rejected);
        machine.Configure(TransactionStatus.Sending)
            .Permit(PaymentOperation.Accept, TransactionStatus.Accepted)
            .Permit(PaymentOperation.Reject, TransactionStatus.Rejected)
            .Permit(PaymentOperation.NotSent, TransactionStatus.NotSent)
            .Permit(PaymentOperation.OutcomeUnknown, TransactionStatus.Uncertain)
            .Permit(PaymentOperation.RetryConnection, TransactionStatus.Received);
        machine.Configure(TransactionStatus.Uncertain)
            .Permit(PaymentOperation.BeginInvestigation, TransactionStatus.Investigating)
            .Permit(PaymentOperation.BeginResending, TransactionStatus.Resending)
            .Permit(PaymentOperation.RequireManualReview, TransactionStatus.ManualReview);
        machine.Configure(TransactionStatus.Investigating)
            .Permit(PaymentOperation.Accept, TransactionStatus.Accepted)
            .Permit(PaymentOperation.Reject, TransactionStatus.Rejected)
            .Permit(PaymentOperation.OutcomeUnknown, TransactionStatus.Uncertain)
            .Permit(PaymentOperation.BeginResending, TransactionStatus.Resending)
            .Permit(PaymentOperation.RequireManualReview, TransactionStatus.ManualReview);
        machine.Configure(TransactionStatus.Resending)
            .Permit(PaymentOperation.Accept, TransactionStatus.Accepted)
            .Permit(PaymentOperation.Reject, TransactionStatus.Rejected)
            .Permit(PaymentOperation.OutcomeUnknown, TransactionStatus.Uncertain)
            .Permit(PaymentOperation.RequireManualReview, TransactionStatus.ManualReview);
        foreach (var state in new[] { TransactionStatus.Accepted, TransactionStatus.Rejected, TransactionStatus.NotSent, TransactionStatus.ManualReview })
            machine.Configure(state).Permit(PaymentOperation.ResolveManually, TransactionStatus.ManuallyResolved);
        return machine;
    }
}
