using IPS.Middleware.Infrastructure.Inbound;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

public sealed class InboundSchedulingTests
{
    [Fact]
    public async Task Queue_is_bounded_fifo_coalesces_ids_and_allows_rediscovery_after_dequeue()
    {
        var queue = new InboundProcessingChannel(new(2, 2));
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        Assert.True(queue.TryNotify(first));
        Assert.False(queue.TryNotify(first));
        Assert.True(queue.TryNotify(second));
        Assert.False(queue.TryNotify(Guid.NewGuid()));
        Assert.Equal(first, await queue.ReadAsync(default));
        Assert.Equal(second, await queue.ReadAsync(default));
        Assert.True(queue.TryNotify(first));
        Assert.True(queue.TryRead(out var repeated));
        Assert.Equal(first, repeated);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await queue.ReadAsync(cancelled.Token));
    }

    [Fact]
    public void Defaults_and_invalid_configuration_are_explicit()
    {
        var options = new InboundSchedulingOptions();
        Assert.Equal(256, options.Capacity);
        Assert.Equal(100, options.DiscoveryBatch);
        Assert.Equal(TimeSpan.FromSeconds(1), options.DiscoveryInterval);
        Assert.Equal(TimeSpan.FromSeconds(45), options.ClaimDuration);
        Assert.Throws<ArgumentOutOfRangeException>(() => new InboundSchedulingOptions(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InboundSchedulingOptions(discoveryBatch: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InboundSchedulingOptions(1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InboundSchedulingOptions(discoveryInterval: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InboundSchedulingOptions(claimDuration: TimeSpan.FromSeconds(-1)));
    }
}
