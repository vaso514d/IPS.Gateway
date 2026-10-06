using Stateless;

namespace IPS.Middleware.Domain.Transactions;

public sealed class OutgoingPayment : AggregateRoot
{
    private StateMachine<TransactionStatus, PaymentOperation>? _machine;

    private OutgoingPayment()
    {
    }

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

        Raise(new PaymentReceived(MessageType, ClientReference), CreatedAtUtc);
        CurrentSequence = EventSequence;
    }

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

    public PaymentOutcome Current => new(
        CurrentStatus,
        CurrentSource,
        CurrentStatusAtUtc,
        CurrentSequence,
        new PaymentDetails(CurrentReasonCode, CurrentIpsInternalCode, CurrentDescription));

    public bool IsFinal => CurrentStatus is TransactionStatus.Accepted
        or TransactionStatus.Rejected
        or TransactionStatus.NotSent
        or TransactionStatus.ManuallyResolved;

    public bool AwaitsOutcome => CurrentStatus is TransactionStatus.Sending
        or TransactionStatus.Uncertain
        or TransactionStatus.Investigating
        or TransactionStatus.Resending;

    public static OutgoingPayment Receive(Guid id, string messageType, string clientReference, DateTimeOffset at) =>
        new(id, messageType, clientReference, at);

    public void BeginSending(DateTimeOffset at) =>
        Transition(PaymentOperation.BeginSending, StatusSource.Gateway, at);

    public void RecordAcceptance(StatusSource source, DateTimeOffset at, PaymentDetails? details = null) =>
        RecordOutcome(PaymentOperation.Accept, TransactionStatus.Accepted, source, at, details);

    public void RecordRejection(StatusSource source, DateTimeOffset at, PaymentDetails? details = null) =>
        RecordOutcome(PaymentOperation.Reject, TransactionStatus.Rejected, source, at, details);

    // An IPS report settles a payment that is still awaiting its outcome; any other payment only observes it.
    public void RecordReport(TransactionStatus reported, StatusSource source, DateTimeOffset at, PaymentDetails? details = null)
    {
        if (AwaitsOutcome)
        {
            var operation = reported == TransactionStatus.Accepted ? PaymentOperation.Accept : PaymentOperation.Reject;
            RecordOutcome(operation, reported, source, at, details);
            return;
        }

        Observe(reported, source, at, details);
    }

    public void ScheduleConnectionRetry(DateTimeOffset at, PaymentDetails? details = null) =>
        Transition(PaymentOperation.RetryConnection, StatusSource.Gateway, at, details);

    public void RecordNotSent(DateTimeOffset at, PaymentDetails? details = null) =>
        RecordOutcome(PaymentOperation.NotSent, TransactionStatus.NotSent, StatusSource.Gateway, at, details);

    public void MarkOutcomeUnknown(StatusSource source, DateTimeOffset at, PaymentDetails? details = null) =>
        Transition(PaymentOperation.OutcomeUnknown, source, at, details);

    public void BeginInvestigation(DateTimeOffset at) =>
        Transition(PaymentOperation.BeginInvestigation, StatusSource.Recovery, at);

    public void BeginResending(StatusSource source, DateTimeOffset at, PaymentDetails? details = null) =>
        Transition(PaymentOperation.BeginResending, source, at, details);

    public void RequireManualReview(DateTimeOffset at, PaymentDetails? details = null) =>
        Transition(PaymentOperation.RequireManualReview, StatusSource.Recovery, at, details);

    public void ResolveManually(DateTimeOffset at, PaymentDetails? details = null) =>
        RecordOutcome(PaymentOperation.ResolveManually, TransactionStatus.ManuallyResolved, StatusSource.Operator, at, details);

    public void RecordStep(ProcessingStep step, DateTimeOffset at)
    {
        RequireDefined(step);
        Raise(new PaymentProcessingObserved(step, CurrentStatus), at);
    }

    // A technical failure leaves the current state unchanged and retryable.
    public void RecordProcessingFailure(ProcessingStep step, DateTimeOffset at, string description)
    {
        RequireDefined(step);
        var normalized = new PaymentDetails(description: description).Description
            ?? throw new ArgumentException("A failure description is required.", nameof(description));

        Raise(new PaymentProcessingFailed(step, CurrentStatus, normalized), at);
    }

    private void RecordOutcome(
        PaymentOperation operation,
        TransactionStatus reported,
        StatusSource source,
        DateTimeOffset at,
        PaymentDetails? details)
    {
        if (IsObservationOnly(operation))
        {
            Observe(reported, source, at, details);
            return;
        }

        Transition(operation, source, at, details);
    }

    // A report that does not change the payment is recorded, flagged when it contradicts the current status.
    private void Observe(TransactionStatus reported, StatusSource source, DateTimeOffset at, PaymentDetails? details)
    {
        RequireDefined(source);
        Raise(new PaymentOutcomeObserved(CurrentStatus, reported, CurrentStatus != reported, source, details ?? new()), at);
    }

    // Operator resolution is an explicit correction; any other final report on a final payment is only observed.
    private bool IsObservationOnly(PaymentOperation operation) =>
        operation == PaymentOperation.ResolveManually
            ? CurrentStatus == TransactionStatus.ManuallyResolved
            : IsFinal;

    private void Transition(PaymentOperation operation, StatusSource source, DateTimeOffset at, PaymentDetails? details = null)
    {
        RequireDefined(source);
        var machine = _machine ??= ConfigureMachine();
        if (!machine.CanFire(operation))
        {
            throw new PaymentTransitionException(CurrentStatus, operation);
        }

        var previousStatus = CurrentStatus;
        var reason = details ?? new PaymentDetails();
        machine.Fire(operation);
        CurrentSource = source;
        CurrentStatusAtUtc = at.ToUniversalTime();
        CurrentReasonCode = reason.ReasonCode;
        CurrentIpsInternalCode = reason.IpsInternalCode;
        CurrentDescription = reason.Description;

        Raise(new PaymentStateChanged(operation, previousStatus, CurrentStatus, source, reason), CurrentStatusAtUtc);
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
            .Permit(PaymentOperation.Accept, TransactionStatus.Accepted)
            .Permit(PaymentOperation.Reject, TransactionStatus.Rejected)
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

        TransactionStatus[] manuallyResolvable =
        [
            TransactionStatus.Accepted,
            TransactionStatus.Rejected,
            TransactionStatus.NotSent,
            TransactionStatus.ManualReview
        ];
        foreach (var status in manuallyResolvable)
        {
            machine.Configure(status).Permit(PaymentOperation.ResolveManually, TransactionStatus.ManuallyResolved);
        }

        return machine;
    }

    private static void RequireDefined<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Unknown {typeof(TEnum).Name}.");
        }
    }
}
