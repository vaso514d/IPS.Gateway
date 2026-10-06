using IPS.Middleware.Domain.Inbound;
using Xunit;

namespace IPS.Middleware.Tests.Inbound;

public sealed class IncomingFiTransferTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static IncomingFiTransfer Registered() => IncomingFiTransfer.Register(Guid.NewGuid(), " bagage22 ", "E2E-1", At);

    private static CorePaymentResult Result(CoreOutcome status) => new(status, At, "CORE-1", status == CoreOutcome.Rejected ? "AC01" : null);

    [Fact]
    public void A_new_transfer_is_normalized_and_waits_for_its_first_submission()
    {
        var transfer = Registered();

        Assert.Equal("BAGAGE22", transfer.ParticipantBic);
        Assert.Equal("E2E-1", transfer.EndToEndId);
        Assert.Equal(CoreOutcome.NotSubmitted, transfer.CoreStatus);
        Assert.False(transfer.IsFinal);
        Assert.IsType<IncomingTransferRegistered>(Assert.Single(transfer.PendingEvents));
    }

    [Fact]
    public void A_submission_is_marked_once_and_counts_as_an_attempt()
    {
        var transfer = Registered();

        transfer.BeginSubmission(At);

        Assert.Equal(CoreOutcome.SubmissionStarted, transfer.CoreStatus);
        Assert.Equal(1, transfer.Attempts);
        Assert.Throws<InvalidOperationException>(() => transfer.BeginSubmission(At));
    }

    [Fact]
    public void The_core_is_queried_only_after_a_submission()
    {
        var transfer = Registered();
        Assert.Throws<InvalidOperationException>(transfer.BeginQuery);

        transfer.BeginSubmission(At);
        transfer.RecordCoreResult(Result(CoreOutcome.Unknown), At);
        transfer.BeginQuery();

        Assert.Equal(2, transfer.Attempts);
    }

    [Theory]
    [InlineData(CoreOutcome.Accepted)]
    [InlineData(CoreOutcome.Rejected)]
    public void A_final_outcome_is_recorded_once_and_closes_the_transfer(CoreOutcome outcome)
    {
        var transfer = Registered();
        transfer.BeginSubmission(At);

        transfer.RecordCoreResult(Result(outcome), At);

        Assert.True(transfer.IsFinal);
        Assert.Equal("CORE-1", transfer.CoreReference);
        Assert.Throws<InvalidOperationException>(() => transfer.RecordCoreResult(Result(outcome), At));
        Assert.Throws<InvalidOperationException>(transfer.BeginQuery);
        Assert.Throws<InvalidOperationException>(() => transfer.RequireManualReview("late", At));
        Assert.Throws<InvalidOperationException>(() => transfer.RequireResubmission(At));
    }

    [Fact]
    public void An_outcome_needs_a_submission_and_a_supported_status()
    {
        var transfer = Registered();
        Assert.Throws<InvalidOperationException>(() => transfer.RecordCoreResult(Result(CoreOutcome.Accepted), At));

        transfer.BeginSubmission(At);
        Assert.Throws<InvalidOperationException>(() => transfer.RecordCoreResult(Result(CoreOutcome.SubmissionStarted), At));
    }

    [Fact]
    public void An_unanswered_submission_can_be_repeated_after_the_core_says_it_never_saw_it()
    {
        var transfer = Registered();
        transfer.BeginSubmission(At);
        transfer.RecordCoreResult(Result(CoreOutcome.Unknown), At);

        transfer.RequireResubmission(At);
        transfer.BeginSubmission(At);

        Assert.Equal(CoreOutcome.SubmissionStarted, transfer.CoreStatus);
        Assert.Equal(2, transfer.Attempts);
    }

    [Fact]
    public void Manual_review_closes_an_unsettled_transfer()
    {
        var transfer = Registered();
        transfer.BeginSubmission(At);

        transfer.RequireManualReview("window ended", At);

        Assert.True(transfer.IsFinal);
        Assert.Equal("window ended", transfer.ManualReviewReason);
        Assert.Equal(CoreOutcome.SubmissionStarted, transfer.CoreStatus);
    }
}
