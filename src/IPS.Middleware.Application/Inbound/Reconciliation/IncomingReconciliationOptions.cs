namespace IPS.Middleware.Application.Inbound.Reconciliation;

public sealed class IncomingReconciliationOptions
{
    public IncomingReconciliationOptions(
        TimeSpan? callTimeout = null,
        TimeSpan? persistenceBudget = null,
        TimeSpan? ownership = null,
        TimeSpan? window = null,
        int discoveryBatch = 50,
        TimeSpan[]? retryDelays = null,
        TimeSpan? repeatInterval = null)
    {
        CallTimeout = callTimeout ?? TimeSpan.FromSeconds(20);
        PersistenceBudget = persistenceBudget ?? TimeSpan.FromSeconds(2);
        Ownership = ownership ?? TimeSpan.FromSeconds(45);
        Window = window ?? TimeSpan.FromHours(24);
        DiscoveryBatch = discoveryBatch;
        RetryDelays = Array.AsReadOnly((retryDelays ?? [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5)]).ToArray());
        RepeatInterval = repeatInterval ?? TimeSpan.FromMinutes(15);
        TimeSpan[] budgets = [CallTimeout, PersistenceBudget, Window, RepeatInterval, .. RetryDelays];
        var fitsOwnership = Ownership > CallTimeout + PersistenceBudget;
        if (budgets.Any(budget => budget <= TimeSpan.Zero) || !fitsOwnership || discoveryBatch <= 0)
        {
            throw new ArgumentException("Positive reconciliation budgets must fit within ownership.");
        }
    }

    public TimeSpan CallTimeout { get; }
    public TimeSpan PersistenceBudget { get; }
    public TimeSpan Ownership { get; }
    public TimeSpan Window { get; }
    public int DiscoveryBatch { get; }
    public IReadOnlyList<TimeSpan> RetryDelays { get; }
    public TimeSpan RepeatInterval { get; }

    // Configured delays apply to the first attempts; later attempts repeat at the steady interval.
    public TimeSpan RetryDelay(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attempts);
        return attempts <= RetryDelays.Count ? RetryDelays[attempts - 1] : RepeatInterval;
    }
}
