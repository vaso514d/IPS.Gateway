using IPS.Middleware.Application.Inbound.Processing;
using Xunit;

namespace IPS.Middleware.Tests.Inbound;

public sealed class ProcessingBudgetTests
{
    private static readonly DateTimeOffset Deadline = new(2026, 10, 5, 12, 0, 20, TimeSpan.Zero);
    private static readonly ProcessingBudget Budget = new(Deadline, new IncomingProcessingOptions());

    [Theory]
    [InlineData(20_000, 15_000, 3_000, true)]  // fresh payment
    [InlineData(5_001, 1, 3_000, true)]        // last millisecond of the submission budget
    [InlineData(5_000, 0, 3_000, true)]        // submission exhausted, status still capped at 3 s
    [InlineData(4_000, 0, 2_000, true)]        // status limited by the reply reserve
    [InlineData(2_001, 0, 1, true)]            // last millisecond before the reply cutoff
    [InlineData(2_000, 0, 0, false)]           // reply cutoff
    [InlineData(-1_000, 0, 0, false)]          // after the deadline
    public void Budgets_are_measured_back_from_the_frozen_deadline(int beforeDeadlineMs, int submissionMs, int statusMs, bool withinReplyWindow)
    {
        var now = Deadline.AddMilliseconds(-beforeDeadlineMs);
        Assert.Equal(TimeSpan.FromMilliseconds(submissionMs), Budget.Submission(now));
        Assert.Equal(TimeSpan.FromMilliseconds(statusMs), Budget.Status(now));
        Assert.Equal(withinReplyWindow, Budget.WithinReplyWindow(now));
    }
}
