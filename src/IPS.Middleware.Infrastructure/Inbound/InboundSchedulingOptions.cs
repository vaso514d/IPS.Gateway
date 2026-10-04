namespace IPS.Middleware.Infrastructure.Inbound;

public sealed class InboundSchedulingOptions
{
    public InboundSchedulingOptions(int capacity = 256, int discoveryBatch = 100,
        TimeSpan? discoveryInterval = null, TimeSpan? claimDuration = null, int registrationMaxAttempts = 8)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(discoveryBatch);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(discoveryBatch, capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(registrationMaxAttempts);
        RegistrationMaxAttempts = registrationMaxAttempts;
        Capacity = capacity;
        DiscoveryBatch = discoveryBatch;
        DiscoveryInterval = discoveryInterval ?? TimeSpan.FromSeconds(1);
        ClaimDuration = claimDuration ?? TimeSpan.FromSeconds(45);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(DiscoveryInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ClaimDuration, TimeSpan.Zero);
    }

    public int RegistrationMaxAttempts { get; }
    public int Capacity { get; }
    public int DiscoveryBatch { get; }
    public TimeSpan DiscoveryInterval { get; }
    public TimeSpan ClaimDuration { get; }
}
