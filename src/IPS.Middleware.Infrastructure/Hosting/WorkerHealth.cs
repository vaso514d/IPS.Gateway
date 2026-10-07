namespace IPS.Middleware.Infrastructure.Hosting;

public enum WorkerState
{
    Disabled,
    NotStarted,
    Running,
    Stalled,
    Draining,
    Stopped,
    Faulted
}

// What readiness knows about one supervised service. Only a running, progressing or disabled service is healthy.
public sealed record WorkerHealth(string Name, WorkerState State, string? Detail)
{
    public bool IsHealthy => State is WorkerState.Disabled or WorkerState.Running;
}
