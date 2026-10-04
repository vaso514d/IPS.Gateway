using IPS.Middleware.Application.Payments.Pacs008;
using Xunit;

namespace IPS.Middleware.Tests.Payments;

public sealed class Pacs008OptionsTests
{
    [Fact]
    public void Defaults_preserve_the_source_window_and_reject_non_positive_durations()
    {
        var options = new Pacs008Options();
        Assert.Equal((TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(1)),
            (options.SubmissionWindow, options.Ownership, options.PreparationRetryDelay));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pacs008Options(submissionWindow: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pacs008Options(ownership: TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pacs008Options(preparationRetryDelay: TimeSpan.Zero));
    }
}
