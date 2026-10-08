using Xunit;

namespace IPS.Middleware.AspireTests;

// The performance smoke test runs only on request (IPS_PERF=1): it takes minutes and its timing depends on the machine, so it
// must not make the normal suite slower. Like every Aspire test it also needs Docker.
[AttributeUsage(AttributeTargets.Method)]
public sealed class PerformanceFactAttribute : FactAttribute
{
    public PerformanceFactAttribute()
    {
        Skip = Environment.GetEnvironmentVariable("IPS_PERF") == "1"
            ? DockerFactAttribute.UnavailableReason
            : "The performance smoke test runs only when the environment variable IPS_PERF is 1.";
    }
}
