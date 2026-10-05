using IPS.Middleware.Application.Payments.Investigation;
using Xunit;

namespace IPS.Middleware.Tests.Payments;

public sealed class InvestigationOptionsTests
{
    [Fact]
    public void Defaults_preserve_schedule_and_do_not_expose_mutable_delays()
    {
        var input = new[] { TimeSpan.FromSeconds(30) };
        var options = new InvestigationOptions(retryDelays: input);
        input[0] = TimeSpan.Zero; options.RetryDelays[0] = TimeSpan.Zero;
        Assert.Equal(TimeSpan.FromSeconds(30), options.RetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(15), options.RetryDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(9), options.FirstDelay);
        var defaults = new InvestigationOptions();
        Assert.Equal(TimeSpan.FromMinutes(1), defaults.RetryDelay(2));
        Assert.Equal(TimeSpan.FromMinutes(5), defaults.RetryDelay(3));
        Assert.Equal(TimeSpan.FromMinutes(15), defaults.RetryDelay(4));
    }
    [Fact]
    public void Rejects_invalid_counts_durations_and_timeout_ordering()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InvestigationOptions(firstDelay: TimeSpan.FromSeconds(8)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InvestigationOptions(retryDelays: [TimeSpan.Zero]));
        Assert.Throws<ArgumentException>(() => new InvestigationOptions(maxCycles: -1));
        Assert.Throws<ArgumentException>(() => new InvestigationOptions(discoveryBatch: 0));
        Assert.Throws<ArgumentException>(() => new InvestigationOptions(callTimeout: TimeSpan.FromSeconds(35)));
        Assert.Throws<ArgumentException>(() => new InvestigationOptions(ownership: TimeSpan.FromSeconds(37)));
    }
}
