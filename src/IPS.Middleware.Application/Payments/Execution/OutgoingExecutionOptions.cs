namespace IPS.Middleware.Application.Payments.Execution;

public sealed class OutgoingExecutionOptions
{
    public OutgoingExecutionOptions(
        bool enabled = false,
        int concurrency = 8,
        int callbackConcurrency = 8,
        int channelCapacity = 256,
        int discoveryBatch = 100,
        TimeSpan? httpWait = null,
        TimeSpan? attemptBudget = null,
        TimeSpan? discoveryInterval = null,
        TimeSpan? statusPollInterval = null,
        TimeSpan? shutdownBudget = null)
    {
        var validCapacity = concurrency is >= 1 and <= 1000 && callbackConcurrency is >= 1 and <= 1000 && channelCapacity >= 1;
        var validDiscovery = discoveryBatch is >= 1 and <= 1000 && discoveryBatch <= channelCapacity;
        if (!validCapacity || !validDiscovery)
        {
            throw new ArgumentOutOfRangeException(nameof(concurrency), "Invalid execution capacity or discovery batch.");
        }

        Enabled = enabled;
        Concurrency = concurrency;
        CallbackConcurrency = callbackConcurrency;
        ChannelCapacity = channelCapacity;
        DiscoveryBatch = discoveryBatch;
        HttpWait = Positive(httpWait ?? TimeSpan.FromSeconds(30));
        AttemptBudget = Positive(attemptBudget ?? TimeSpan.FromSeconds(35));
        DiscoveryInterval = Positive(discoveryInterval ?? TimeSpan.FromSeconds(1));
        StatusPollInterval = Positive(statusPollInterval ?? TimeSpan.FromMilliseconds(100));
        ShutdownBudget = Positive(shutdownBudget ?? TimeSpan.FromSeconds(30));
        if (HttpWait >= AttemptBudget)
        {
            throw new ArgumentException("HTTP wait must be shorter than the attempt budget.");
        }
    }

    public bool Enabled { get; }
    public int Concurrency { get; }
    public int CallbackConcurrency { get; }
    public int ChannelCapacity { get; }
    public int DiscoveryBatch { get; }
    public TimeSpan HttpWait { get; }
    public TimeSpan AttemptBudget { get; }
    public TimeSpan DiscoveryInterval { get; }
    public TimeSpan StatusPollInterval { get; }
    public TimeSpan ShutdownBudget { get; }

    private static TimeSpan Positive(TimeSpan value) =>
        value > TimeSpan.Zero && value <= TimeSpan.FromDays(1) ? value : throw new ArgumentOutOfRangeException(nameof(value));
}
