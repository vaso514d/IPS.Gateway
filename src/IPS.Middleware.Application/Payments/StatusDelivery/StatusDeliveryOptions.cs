namespace IPS.Middleware.Application.Payments.StatusDelivery;

public sealed class StatusDeliveryOptions
{
    public StatusDeliveryOptions(
        int attemptsPerRound = 3,
        int maxRounds = 0,
        TimeSpan? delayBetweenAttempts = null,
        TimeSpan? pauseBetweenRounds = null,
        TimeSpan? callTimeout = null,
        TimeSpan? persistenceBudget = null,
        TimeSpan? ownership = null,
        int discoveryBatch = 50,
        TimeSpan? discoveryInterval = null)
    {
        if (attemptsPerRound < 1 || maxRounds < 0 || discoveryBatch is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptsPerRound));
        }

        AttemptsPerRound = attemptsPerRound;
        MaxRounds = maxRounds;
        DiscoveryBatch = discoveryBatch;
        DelayBetweenAttempts = Positive(delayBetweenAttempts ?? TimeSpan.FromSeconds(5));
        PauseBetweenRounds = Positive(pauseBetweenRounds ?? TimeSpan.FromMinutes(10));
        CallTimeout = Positive(callTimeout ?? TimeSpan.FromSeconds(20));
        PersistenceBudget = Positive(persistenceBudget ?? TimeSpan.FromSeconds(2));
        Ownership = Positive(ownership ?? TimeSpan.FromSeconds(45));
        DiscoveryInterval = Positive(discoveryInterval ?? TimeSpan.FromSeconds(5));
        if (Ownership <= CallTimeout + PersistenceBudget)
        {
            throw new ArgumentException("Delivery ownership must exceed call and persistence budgets.");
        }
    }
    public int AttemptsPerRound { get; }
    public int MaxRounds { get; }
    public int DiscoveryBatch { get; }
    public TimeSpan DelayBetweenAttempts { get; }
    public TimeSpan PauseBetweenRounds { get; }
    public TimeSpan CallTimeout { get; }
    public TimeSpan PersistenceBudget { get; }
    public TimeSpan Ownership { get; }
    public TimeSpan DiscoveryInterval { get; }

    public StatusDeliveryRetry AfterFailure(int attempts, DateTimeOffset at)
    {
        if (attempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts));
        }

        return MaxRounds > 0 && (long)attempts >= (long)AttemptsPerRound * MaxRounds
            ? new(StatusDeliveryState.Exhausted, null)
            : new(StatusDeliveryState.Pending, at.ToUniversalTime() + (attempts % AttemptsPerRound == 0 ? PauseBetweenRounds : DelayBetweenAttempts));
    }
    private static TimeSpan Positive(TimeSpan value) => value > TimeSpan.Zero && value <= TimeSpan.FromDays(365)
        ? value : throw new ArgumentOutOfRangeException(nameof(value));
}
