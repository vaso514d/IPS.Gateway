using IPS.Middleware.Domain.Inbound;
using Xunit;

namespace IPS.Middleware.Tests.Inbound;

public sealed class IncomingProcessingStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private const string NoFinalResult = "Core system did not return a final payment result within the reply window.";
    [Theory]
    [InlineData("none", true, false, "MS03", NoFinalResult, IncomingFollowUp.None)]
    [InlineData("started", true, false, "MS03", NoFinalResult, IncomingFollowUp.ReconciliationRequired)]
    [InlineData("unknown", true, false, "MS03", NoFinalResult, IncomingFollowUp.ReconciliationRequired)]
    [InlineData("unknown", false, false, "MS03", NoFinalResult, IncomingFollowUp.ReconciliationRequired)]
    [InlineData("accepted", true, true, null, "credited", IncomingFollowUp.None)]
    [InlineData("accepted", false, false, "MS03", NoFinalResult, IncomingFollowUp.ReversalRequired)]
    [InlineData("rejected:AC01", true, false, "AC01", "refused", IncomingFollowUp.None)]
    [InlineData("rejected: ", true, false, "MS03", "refused", IncomingFollowUp.None)]
    [InlineData("rejected:AC01", false, false, "MS03", NoFinalResult, IncomingFollowUp.None)]
    public void Ips_decision_follows_a_final_core_outcome_only_inside_the_reply_window(string core, bool withinReplyWindow, bool accepted, string? reason, string description, IncomingFollowUp followUp)
    {
        var payment = IncomingPayment.Register(Guid.NewGuid(), "BAGAGE22", "E2E", Now);
        if (core != "none")
        {
            payment.BeginSubmission(Now);
        }

        if (core == "unknown")
        {
            payment.RecordCoreResult(new(CoreOutcome.Unknown, Now, description: "timeout"), Now);
        }

        if (core == "accepted")
        {
            payment.RecordCoreResult(new(CoreOutcome.Accepted, Now, description: "credited"), Now);
        }

        if (core.StartsWith("rejected:", StringComparison.Ordinal))
        {
            payment.RecordCoreResult(new(CoreOutcome.Rejected, Now, reasonCode: core["rejected:".Length..], description: "refused"), Now);
        }

        payment.DecideIps(withinReplyWindow, Now.AddSeconds(1));
        Assert.Equal(new IncomingIpsDecision(accepted, Now.AddSeconds(1), reason, description), payment.IpsDecision);
        Assert.Equal(followUp, payment.FollowUp);
    }

    [Fact]
    public void Late_credit_keeps_the_credit_separate_from_rejection_and_requires_reversal()
    {
        var payment = IncomingPayment.Register(Guid.NewGuid(), "BAGAGE22", "E2E", Now);
        payment.BeginSubmission(Now);
        payment.RecordCoreResult(new(CoreOutcome.Accepted, Now.AddSeconds(5), coreReference: "CBS-17"), Now.AddSeconds(45));
        payment.DecideIps(false, Now.AddSeconds(45));
        Assert.Equal(CoreOutcome.Accepted, payment.CoreStatus);
        Assert.False(payment.IpsDecision!.Accepted);
        Assert.Equal("MS03", payment.IpsDecision.ReasonCode);
        Assert.Equal(IncomingFollowUp.ReversalRequired, payment.FollowUp);
        var decision = payment.IpsDecision;
        payment.DecideIps(true, Now.AddMinutes(1));
        Assert.Equal(decision, payment.IpsDecision);
    }

    [Fact]
    public void Repeated_and_conflicting_results_preserve_the_original_core_outcome_and_ips_decision()
    {
        var payment = IncomingPayment.Register(Guid.NewGuid(), "BAGAGE22", "E2E", Now);
        payment.BeginSubmission(Now);
        var first = new CorePaymentResult(CoreOutcome.Accepted, Now, "FIRST");
        payment.RecordCoreResult(first, Now);
        payment.DecideIps(true, Now);
        var decision = payment.IpsDecision;
        payment.RecordCoreResult(new CorePaymentResult(first) { ProcessedAtUtc = Now.AddMinutes(1), CoreReference = "SECOND" }, Now.AddMinutes(1));
        Assert.Equal(first, payment.CoreResult);
        payment.RecordCoreResult(new(CoreOutcome.Rejected, Now.AddMinutes(2)), Now.AddMinutes(2));
        Assert.Equal(first, payment.CoreResult);
        Assert.Equal(decision, payment.IpsDecision);
        Assert.Equal(IncomingFollowUp.ManualReviewRequired, payment.FollowUp);
        Assert.Equal(6, payment.EventSequence);
    }

    [Fact]
    public void Conflict_before_ips_decision_preserves_manual_review()
    {
        var payment = IncomingPayment.Register(Guid.NewGuid(), "BAGAGE22", "E2E", Now);
        payment.BeginSubmission(Now);
        payment.RecordCoreResult(new(CoreOutcome.Accepted, Now), Now);
        payment.RecordCoreResult(new(CoreOutcome.Rejected, Now.AddSeconds(1)), Now.AddSeconds(1));
        payment.DecideIps(true, Now.AddSeconds(2));
        Assert.Equal(CoreOutcome.Accepted, payment.CoreStatus);
        Assert.True(payment.IpsDecision!.Accepted);
        Assert.Equal(IncomingFollowUp.ManualReviewRequired, payment.FollowUp);
    }

    [Fact]
    public void Invalid_operations_leave_state_and_events_unchanged()
    {
        var payment = IncomingPayment.Register(Guid.NewGuid(), "BAGAGE22", "E2E", Now);
        Assert.Throws<InvalidOperationException>(() => payment.RecordCoreResult(new(CoreOutcome.Accepted, Now), Now));
        Assert.Equal(1, payment.EventSequence);
        payment.BeginSubmission(Now);
        Assert.Throws<InvalidOperationException>(() => payment.BeginSubmission(Now));
        Assert.Throws<InvalidOperationException>(() => payment.RecordCoreResult(new(CoreOutcome.NotSubmitted, Now), Now));
        Assert.Equal(2, payment.EventSequence);
    }
}
