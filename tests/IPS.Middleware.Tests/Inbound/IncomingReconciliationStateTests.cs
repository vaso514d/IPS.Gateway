using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Domain.Inbound;
using Xunit;

namespace IPS.Middleware.Tests.Inbound;

public sealed class IncomingReconciliationStateTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData(ReversalDelivery.Accepted)]
    [InlineData(ReversalDelivery.Unsuccessful)]
    [InlineData(ReversalDelivery.Uncertain)]
    public void Reversal_delivery_keeps_original_credit_and_rejection_and_requires_manual_review(ReversalDelivery delivery)
    {
        var payment = Credit();
        var original = payment.CoreResult;
        var decision = payment.IpsDecision;
        payment.BeginReversal(Now);
        payment.RecordReversalDelivery(delivery, Now.AddSeconds(1));
        Assert.Equal(original, payment.CoreResult);
        Assert.Equal(decision, payment.IpsDecision);
        Assert.Equal(delivery, payment.Reversal);
        Assert.Equal(IncomingFollowUp.ManualReviewRequired, payment.FollowUp);
        Assert.NotNull(payment.ManualReviewReason);
        var count = payment.EventSequence;
        Assert.Throws<InvalidOperationException>(() => payment.BeginReversal(Now));
        Assert.Throws<InvalidOperationException>(() => payment.RecordReversalDelivery(delivery, Now));
        Assert.Equal(count, payment.EventSequence);
        var occurrence = Assert.IsType<IncomingReconciliationRecorded>(payment.PendingEvents.Last());
        Assert.Equal(delivery, occurrence.Reversal);
        Assert.Equal(decision, occurrence.IpsDecision);
    }

    [Fact]
    public void Unsubmitted_or_unmarked_payments_cannot_record_reversal()
    {
        var payment = IncomingPayment.Register(Guid.NewGuid(), "BAGAGE22", "E2E", Now);
        Assert.Throws<InvalidOperationException>(() => payment.BeginReversal(Now));
        Assert.Throws<InvalidOperationException>(() => payment.RequireManualReview("reason", Now));
        Assert.Throws<InvalidOperationException>(() => Credit().RecordReversalDelivery(ReversalDelivery.Accepted, Now));
        Assert.Equal(1, payment.EventSequence);
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 300)]
    [InlineData(4, 900)]
    [InlineData(99, 900)]
    public void Retry_schedule_is_source_compatible(int attempts, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), new IncomingReconciliationOptions().RetryDelay(attempts));

    [Fact]
    public void Invalid_budgets_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new IncomingReconciliationOptions(callTimeout: TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new IncomingReconciliationOptions(persistenceBudget: TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new IncomingReconciliationOptions(ownership: TimeSpan.FromSeconds(22)));
        Assert.Throws<ArgumentException>(() => new IncomingReconciliationOptions(window: TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new IncomingReconciliationOptions(discoveryBatch: 0));
    }

    private static IncomingPayment Credit()
    {
        var payment = IncomingPayment.Register(Guid.NewGuid(), "BAGAGE22", "E2E", Now);
        payment.BeginSubmission(Now);
        payment.RecordCoreResult(new(CoreOutcome.Accepted, Now, "CREDIT"), Now);
        payment.DecideIps(false, Now);
        return payment;
    }
}
