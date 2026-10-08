using IPS.Middleware.Domain.Transactions;
using Xunit;

namespace IPS.Middleware.Tests.Transactions;

public sealed class OutgoingPaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 10, 0, 0, TimeSpan.Zero);
    [Fact]
    public void Receive_normalizes_and_collects_one_immutable_event()
    {
        var payment = OutgoingPayment.Receive(Guid.NewGuid(), " pacs.008 ", " CBS-1 ", Now.ToOffset(TimeSpan.FromHours(4)));
        Assert.Equal("pacs.008", payment.MessageType);
        Assert.Equal("CBS-1", payment.ClientReference);
        Assert.Equal(Now, payment.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, payment.CurrentStatusAtUtc.Offset);
        Assert.Equal(TransactionStatus.Received, payment.CurrentStatus);
        var received = Assert.IsType<PaymentReceived>(Assert.Single(payment.PendingEvents));
        Assert.Equal(payment.Id, received.AggregateId);
        Assert.NotEqual(Guid.Empty, received.EventId);
        Assert.Equal(1, received.Sequence);
        var events = Assert.IsAssignableFrom<IList<IPS.Middleware.Domain.DomainEvent>>(payment.PendingEvents);
        Assert.True(events.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => events.Clear());
    }

    [Fact]
    public void Processing_failure_is_a_bounded_observation_that_keeps_state()
    {
        var payment = New();
        payment.BeginSending(Now);
        var before = payment.Current;
        payment.RecordProcessingFailure(ProcessingStep.Signed, Now.AddSeconds(1), " " + new string('x', 2100) + " ");
        var failure = Assert.IsType<PaymentProcessingFailed>(payment.PendingEvents[^1]);
        Assert.Equal((ProcessingStep.Signed, TransactionStatus.Sending, 2000), (failure.Step, failure.Status, failure.Description.Length));
        Assert.Equal(before, payment.Current);
        Assert.Throws<ArgumentException>(() => payment.RecordProcessingFailure(ProcessingStep.Signed, Now, " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => payment.RecordProcessingFailure((ProcessingStep)99, Now, "failure"));
    }

    [Fact]
    public void Invalid_operation_does_not_change_state_or_events()
    {
        var payment = New();
        var before = payment.Current;
        var exception = Assert.Throws<PaymentTransitionException>(() => payment.RecordAcceptance(StatusSource.Ips, Now));
        Assert.Equal(PaymentOperation.Accept, exception.Operation);
        Assert.Equal(before, payment.Current);
        Assert.Single(payment.PendingEvents);
        Assert.Throws<PaymentTransitionException>(() => payment.BeginInvestigation(Now));
    }

    [Fact]
    public void Processing_observations_preserve_outcome_but_advance_event_sequence()
    {
        var payment = New();
        payment.RecordRejection(StatusSource.Gateway, Now, new(" ac01 ", 1009, " Account closed "));
        var outcome = payment.Current;
        payment.RecordStep(ProcessingStep.RepliedToIps, Now.AddSeconds(-1));
        Assert.Equal(outcome, payment.Current);
        Assert.Equal(3, payment.EventSequence);
        var observation = Assert.IsType<PaymentProcessingObserved>(payment.PendingEvents[^1]);
        Assert.Equal(TransactionStatus.Rejected, observation.Status);
        Assert.Equal(Now.AddSeconds(-1), observation.OccurredAtUtc);
        Assert.Equal("AC01", payment.CurrentReasonCode);
    }

    [Fact]
    public void Duplicate_and_conflicting_final_replies_are_distinct_observations()
    {
        var payment = New();
        payment.BeginSending(Now);
        payment.RecordAcceptance(StatusSource.Ips, Now.AddSeconds(1), new(description: "original"));
        var accepted = payment.Current;
        payment.RecordAcceptance(StatusSource.Ips, Now.AddSeconds(2), new(description: "repeat"));
        Assert.False(Assert.IsType<PaymentOutcomeObserved>(payment.PendingEvents[^1]).Conflicting);
        payment.RecordRejection(StatusSource.Ips, Now.AddSeconds(3), new("AC04"));
        var conflict = Assert.IsType<PaymentOutcomeObserved>(payment.PendingEvents[^1]);
        Assert.True(conflict.Conflicting);
        Assert.Equal(TransactionStatus.Rejected, conflict.ReportedStatus);
        Assert.Equal(accepted, payment.Current);
        Assert.Equal(5, payment.EventSequence);
    }

    [Fact]
    public void Safe_retry_resets_outcome_details_and_permits_a_new_send()
    {
        var payment = New();
        payment.BeginSending(Now);
        payment.ScheduleConnectionRetry(Now, new("MS03", description: "No connection"));
        payment.BeginSending(Now.AddSeconds(1));
        Assert.Equal(TransactionStatus.Sending, payment.CurrentStatus);
        Assert.Null(payment.CurrentReasonCode);
        Assert.Null(payment.CurrentDescription);
        Assert.Equal(4, payment.EventSequence);
    }

    [Fact]
    public void Investigation_resending_and_manual_resolution_are_explicit_transitions()
    {
        var payment = New();
        payment.BeginSending(Now);
        payment.MarkOutcomeUnknown(StatusSource.Gateway, Now);
        payment.BeginInvestigation(Now);
        payment.BeginResending(StatusSource.Investigation, Now);
        payment.MarkOutcomeUnknown(StatusSource.Recovery, Now);
        payment.RequireManualReview(Now, new(description: "Attempts exhausted"));
        Assert.False(payment.IsFinal);
        payment.ResolveManually(Now, new(description: "Operator reconciled"));
        Assert.Equal(TransactionStatus.ManuallyResolved, payment.CurrentStatus);
        Assert.Equal(StatusSource.Operator, payment.CurrentSource);
        Assert.True(payment.IsFinal);
        Assert.Equal(8, payment.EventSequence);
    }

    [Theory]
    [InlineData(TransactionStatus.Sending)]
    [InlineData(TransactionStatus.Investigating)]
    [InlineData(TransactionStatus.Resending)]
    public void Inflight_work_can_become_unknown(TransactionStatus status)
    {
        var payment = In(status);
        payment.MarkOutcomeUnknown(StatusSource.Recovery, Now);
        Assert.Equal(TransactionStatus.Uncertain, payment.CurrentStatus);
    }

    [Theory]
    [InlineData(TransactionStatus.Sending)]
    [InlineData(TransactionStatus.Investigating)]
    [InlineData(TransactionStatus.Resending)]
    public void Inflight_work_can_be_accepted_or_rejected(TransactionStatus status)
    {
        var accepted = In(status);
        accepted.RecordAcceptance(StatusSource.Ips, Now);
        Assert.Equal(TransactionStatus.Accepted, accepted.CurrentStatus);
        var rejected = In(status);
        rejected.RecordRejection(StatusSource.Ips, Now, new("AC04"));
        Assert.Equal(TransactionStatus.Rejected, rejected.CurrentStatus);
    }

    [Theory]
    [InlineData(TransactionStatus.Sending)]
    [InlineData(TransactionStatus.Uncertain)]
    [InlineData(TransactionStatus.Investigating)]
    [InlineData(TransactionStatus.Resending)]
    public void An_ips_report_settles_a_payment_that_awaits_its_outcome(TransactionStatus status)
    {
        var accepted = In(status);
        Assert.True(accepted.AwaitsOutcome);
        accepted.RecordReport(TransactionStatus.Accepted, StatusSource.Ips, Now);
        Assert.Equal(TransactionStatus.Accepted, accepted.CurrentStatus);
        Assert.Equal(StatusSource.Ips, accepted.CurrentSource);
        var rejected = In(status);
        rejected.RecordReport(TransactionStatus.Rejected, StatusSource.Ips, Now, new("AC04"));
        Assert.Equal(TransactionStatus.Rejected, rejected.CurrentStatus);
        Assert.Equal("AC04", rejected.CurrentReasonCode);
    }

    [Theory]
    [InlineData(TransactionStatus.Received, TransactionStatus.Accepted, true)]
    [InlineData(TransactionStatus.ManualReview, TransactionStatus.Rejected, true)]
    [InlineData(TransactionStatus.Accepted, TransactionStatus.Rejected, true)]
    [InlineData(TransactionStatus.Accepted, TransactionStatus.Accepted, false)]
    [InlineData(TransactionStatus.NotSent, TransactionStatus.Accepted, true)]
    public void An_ips_report_only_observes_any_other_payment(TransactionStatus status, TransactionStatus reported, bool conflicting)
    {
        var payment = In(status);
        Assert.False(payment.AwaitsOutcome);
        var sequence = payment.EventSequence;
        payment.RecordReport(reported, StatusSource.Ips, Now, new("AC01"));
        Assert.Equal(status, payment.CurrentStatus);
        var observed = Assert.IsType<PaymentOutcomeObserved>(payment.PendingEvents[^1]);
        Assert.Equal(sequence + 1, observed.Sequence);
        Assert.Equal(conflicting, observed.Conflicting);
        Assert.Equal(reported, observed.ReportedStatus);
        Assert.Equal("AC01", observed.Details.ReasonCode);
    }

    [Theory]
    [InlineData(TransactionStatus.Accepted)]
    [InlineData(TransactionStatus.Rejected)]
    [InlineData(TransactionStatus.NotSent)]
    [InlineData(TransactionStatus.ManuallyResolved)]
    public void Final_outcomes_cannot_be_restarted(TransactionStatus status)
    {
        var payment = In(status);
        var count = payment.PendingEvents.Count;
        Assert.Throws<PaymentTransitionException>(() => payment.BeginSending(Now));
        Assert.Throws<PaymentTransitionException>(() => payment.MarkOutcomeUnknown(StatusSource.Recovery, Now));
        Assert.Equal(count, payment.PendingEvents.Count);
        Assert.True(payment.IsFinal);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2000)]
    [InlineData(2001)]
    public void Explanation_and_time_normalization_is_preserved(int length)
    {
        var payment = New();
        payment.RecordRejection(StatusSource.Gateway, Now.ToOffset(TimeSpan.FromHours(4)),
            new(" ac01 ", 1009, "  " + new string('x', length) + "  "));
        Assert.Equal("AC01", payment.CurrentReasonCode);
        Assert.Equal(new string('x', Math.Min(length, 2000)), payment.CurrentDescription);
        Assert.Equal(TimeSpan.Zero, payment.CurrentStatusAtUtc.Offset);
    }

    [Fact]
    public void Validation_fails_without_modification()
    {
        Assert.Throws<ArgumentException>(() => OutgoingPayment.Receive(Guid.Empty, "pacs.008", "CBS-1", Now));
        Assert.Throws<ArgumentException>(() => OutgoingPayment.Receive(Guid.NewGuid(), " ", "CBS-1", Now));
        Assert.Throws<ArgumentException>(() => OutgoingPayment.Receive(Guid.NewGuid(), "pacs.008", " ", Now));
        var payment = New();
        Assert.Throws<ArgumentOutOfRangeException>(() => payment.RecordStep((ProcessingStep)99, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => payment.RecordRejection((StatusSource)99, Now));
        Assert.Single(payment.PendingEvents);
    }

    [Fact]
    public void Acknowledgement_only_clears_the_committed_event_snapshot()
    {
        var payment = New();
        var ids = payment.PendingEvents.Select(e => e.EventId).ToArray();
        payment.BeginSending(Now);
        payment.AcknowledgeCommittedEvents(ids);
        Assert.Equal(2, Assert.Single(payment.PendingEvents).Sequence);
        Assert.Equal(2, payment.EventSequence);
    }

    public static IEnumerable<object[]> TransitionCases()
    {
        var allowed = new Dictionary<TransactionStatus, Dictionary<PaymentOperation, TransactionStatus>>
        {
            [TransactionStatus.Received] = new() { [PaymentOperation.BeginSending] = TransactionStatus.Sending, [PaymentOperation.Reject] = TransactionStatus.Rejected },
            [TransactionStatus.Sending] = new() { [PaymentOperation.Accept] = TransactionStatus.Accepted, [PaymentOperation.Reject] = TransactionStatus.Rejected, [PaymentOperation.NotSent] = TransactionStatus.NotSent, [PaymentOperation.OutcomeUnknown] = TransactionStatus.Uncertain, [PaymentOperation.RetryConnection] = TransactionStatus.Received },
            [TransactionStatus.Uncertain] = new() { [PaymentOperation.Accept] = TransactionStatus.Accepted, [PaymentOperation.Reject] = TransactionStatus.Rejected, [PaymentOperation.BeginInvestigation] = TransactionStatus.Investigating, [PaymentOperation.BeginResending] = TransactionStatus.Resending, [PaymentOperation.RequireManualReview] = TransactionStatus.ManualReview },
            [TransactionStatus.Investigating] = new() { [PaymentOperation.Accept] = TransactionStatus.Accepted, [PaymentOperation.Reject] = TransactionStatus.Rejected, [PaymentOperation.OutcomeUnknown] = TransactionStatus.Uncertain, [PaymentOperation.BeginResending] = TransactionStatus.Resending, [PaymentOperation.RequireManualReview] = TransactionStatus.ManualReview },
            [TransactionStatus.Resending] = new() { [PaymentOperation.Accept] = TransactionStatus.Accepted, [PaymentOperation.Reject] = TransactionStatus.Rejected, [PaymentOperation.OutcomeUnknown] = TransactionStatus.Uncertain, [PaymentOperation.RequireManualReview] = TransactionStatus.ManualReview },
            [TransactionStatus.ManualReview] = new() { [PaymentOperation.ResolveManually] = TransactionStatus.ManuallyResolved },
            [TransactionStatus.Accepted] = new() { [PaymentOperation.ResolveManually] = TransactionStatus.ManuallyResolved },
            [TransactionStatus.Rejected] = new() { [PaymentOperation.ResolveManually] = TransactionStatus.ManuallyResolved },
            [TransactionStatus.NotSent] = new() { [PaymentOperation.ResolveManually] = TransactionStatus.ManuallyResolved },
            [TransactionStatus.ManuallyResolved] = new()
        };
        foreach (var state in allowed.Keys)
        {
            foreach (var operation in Enum.GetValues<PaymentOperation>())
            {
                var observation = state is TransactionStatus.Accepted or TransactionStatus.Rejected or TransactionStatus.NotSent or TransactionStatus.ManuallyResolved
                    && operation is PaymentOperation.Accept or PaymentOperation.Reject or PaymentOperation.NotSent;
                observation |= state == TransactionStatus.ManuallyResolved && operation == PaymentOperation.ResolveManually;
                var permitted = allowed[state].TryGetValue(operation, out var target);
                yield return new object[]
                {
                    state,
                    operation,
                    permitted,
                    observation,
                    permitted ? target : state
                };
            }
        }
    }

    [Theory]
    [MemberData(nameof(TransitionCases))]
    public void Every_business_operation_obeys_the_approved_transition_table(TransactionStatus state, PaymentOperation operation, bool permitted, bool observation, TransactionStatus target)
    {
        var payment = In(state);
        var outcome = payment.Current;
        var count = payment.PendingEvents.Count;
        var sequence = payment.EventSequence;
        if (!permitted && !observation)
        {
            Assert.Throws<PaymentTransitionException>(() => Operate(payment, operation));
            Assert.Equal(outcome, payment.Current);
            Assert.Equal(sequence, payment.EventSequence);
            Assert.Equal(count, payment.PendingEvents.Count);
            return;
        }

        Operate(payment, operation);
        Assert.Equal(target, payment.CurrentStatus);
        Assert.Equal(sequence + 1, payment.EventSequence);
        Assert.Equal(count + 1, payment.PendingEvents.Count);
        if (observation)
        {
            Assert.Equal(outcome, payment.Current);
        }
        else if (operation == PaymentOperation.ResolveManually)
        {
            Assert.Equal(StatusSource.Operator, payment.CurrentSource);
        }
    }

    private static void Operate(OutgoingPayment payment, PaymentOperation operation)
    {
        switch (operation)
        {
            case PaymentOperation.BeginSending:
                payment.BeginSending(Now);
                break;
            case PaymentOperation.Accept:
                payment.RecordAcceptance(StatusSource.Ips, Now);
                break;
            case PaymentOperation.Reject:
                payment.RecordRejection(StatusSource.Ips, Now);
                break;
            case PaymentOperation.RetryConnection:
                payment.ScheduleConnectionRetry(Now);
                break;
            case PaymentOperation.NotSent:
                payment.RecordNotSent(Now);
                break;
            case PaymentOperation.OutcomeUnknown:
                payment.MarkOutcomeUnknown(StatusSource.Recovery, Now);
                break;
            case PaymentOperation.BeginInvestigation:
                payment.BeginInvestigation(Now);
                break;
            case PaymentOperation.BeginResending:
                payment.BeginResending(StatusSource.Investigation, Now);
                break;
            case PaymentOperation.RequireManualReview:
                payment.RequireManualReview(Now);
                break;
            case PaymentOperation.ResolveManually:
                payment.ResolveManually(Now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static OutgoingPayment New() => OutgoingPayment.Receive(Guid.NewGuid(), "pacs.008", "CBS-1", Now);
    private static OutgoingPayment In(TransactionStatus status)
    {
        var payment = New();
        if (status == TransactionStatus.Received)
        {
            return payment;
        }

        if (status == TransactionStatus.Rejected)
        {
            payment.RecordRejection(StatusSource.Gateway, Now);
            return payment;
        }

        payment.BeginSending(Now);
        switch (status)
        {
            case TransactionStatus.Uncertain:
                payment.MarkOutcomeUnknown(StatusSource.Gateway, Now);
                break;
            case TransactionStatus.ManualReview:
                payment.MarkOutcomeUnknown(StatusSource.Gateway, Now);
                payment.RequireManualReview(Now);
                break;
            case TransactionStatus.Accepted:
                payment.RecordAcceptance(StatusSource.Ips, Now);
                break;
            case TransactionStatus.NotSent:
                payment.RecordNotSent(Now);
                break;
            case TransactionStatus.ManuallyResolved:
                payment.RecordNotSent(Now);
                payment.ResolveManually(Now);
                break;
            case TransactionStatus.Investigating:
                payment.MarkOutcomeUnknown(StatusSource.Gateway, Now);
                payment.BeginInvestigation(Now);
                break;
            case TransactionStatus.Resending:
                payment.MarkOutcomeUnknown(StatusSource.Gateway, Now);
                payment.BeginResending(StatusSource.Recovery, Now);
                break;
        }

        return payment;
    }
}
