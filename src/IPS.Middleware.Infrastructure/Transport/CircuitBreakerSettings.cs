namespace IPS.Middleware.Infrastructure.Transport;

public sealed class CircuitBreakerSettings
{
    public double FailureRatio { get; init; } = 0.5;
    public int MinimumThroughput { get; init; } = 10;
    public TimeSpan SamplingDuration { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        if (!double.IsFinite(FailureRatio) || FailureRatio <= 0 || FailureRatio > 1 || MinimumThroughput < 2 ||
            SamplingDuration < TimeSpan.FromMilliseconds(500) || BreakDuration < TimeSpan.FromMilliseconds(500))
        {
            throw new InvalidOperationException("Invalid HTTP circuit-breaker settings.");
        }
    }
}
