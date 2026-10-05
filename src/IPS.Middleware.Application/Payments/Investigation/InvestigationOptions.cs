namespace IPS.Middleware.Application.Payments.Investigation;

public sealed class InvestigationOptions
{
    private readonly TimeSpan[] _retryDelays;

    public InvestigationOptions(
        TimeSpan? firstDelay = null,
        TimeSpan[]? retryDelays = null,
        TimeSpan? repeatInterval = null,
        TimeSpan? window = null,
        int maxCycles = 0,
        TimeSpan? callTimeout = null,
        TimeSpan? attemptBudget = null,
        TimeSpan? ownership = null,
        TimeSpan? persistenceBudget = null,
        TimeSpan? preparationRetryDelay = null,
        int discoveryBatch = 50,
        TimeSpan? discoveryInterval = null)
    {
        FirstDelay = Positive(firstDelay ?? TimeSpan.FromSeconds(9));
        if (FirstDelay < TimeSpan.FromSeconds(9))
        {
            throw new ArgumentOutOfRangeException(nameof(firstDelay));
        }

        _retryDelays = (retryDelays ?? [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5)]).Select(Positive).ToArray();
        RepeatInterval = Positive(repeatInterval ?? TimeSpan.FromMinutes(15));
        Window = Positive(window ?? TimeSpan.FromHours(24));
        CallTimeout = Positive(callTimeout ?? TimeSpan.FromSeconds(25));
        AttemptBudget = Positive(attemptBudget ?? TimeSpan.FromSeconds(35));
        Ownership = Positive(ownership ?? TimeSpan.FromSeconds(45));
        PersistenceBudget = Positive(persistenceBudget ?? TimeSpan.FromSeconds(2));
        PreparationRetryDelay = Positive(preparationRetryDelay ?? TimeSpan.FromSeconds(1));
        DiscoveryInterval = Positive(discoveryInterval ?? TimeSpan.FromSeconds(5));
        var validLimits = maxCycles >= 0 && discoveryBatch is >= 1 and <= 1000 && Window <= TimeSpan.FromHours(24);
        var validTimeouts = CallTimeout < AttemptBudget && AttemptBudget + PersistenceBudget < Ownership;
        if (!validLimits || !validTimeouts)
        {
            throw new ArgumentException("Invalid investigation limits or timeout ordering.");
        }

        MaxCycles = maxCycles;
        DiscoveryBatch = discoveryBatch;
    }

    public TimeSpan FirstDelay { get; }
    public TimeSpan[] RetryDelays => (TimeSpan[])_retryDelays.Clone();
    public TimeSpan RepeatInterval { get; }
    public TimeSpan Window { get; }
    public int MaxCycles { get; }
    public TimeSpan CallTimeout { get; }
    public TimeSpan AttemptBudget { get; }
    public TimeSpan Ownership { get; }
    public TimeSpan PersistenceBudget { get; }
    public TimeSpan PreparationRetryDelay { get; }
    public int DiscoveryBatch { get; }
    public TimeSpan DiscoveryInterval { get; }

    // Configured delays apply to the first cycles; later cycles repeat at the steady interval.
    public TimeSpan RetryDelay(int cycle) =>
        cycle >= 1 && cycle <= _retryDelays.Length ? _retryDelays[cycle - 1] : RepeatInterval;

    private static TimeSpan Positive(TimeSpan value) =>
        value > TimeSpan.Zero && value <= TimeSpan.FromDays(1) ? value : throw new ArgumentOutOfRangeException(nameof(value));
}
