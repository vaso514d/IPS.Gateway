namespace IPS.Middleware.Infrastructure.Diagnostics;

public sealed class DiagnosticsSettings
{
    // Whether the periodic SQL snapshot behind the backlog gauges runs; the other metrics are always recorded.
    public bool BacklogSnapshot { get; init; } = true;
    public TimeSpan SnapshotInterval { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan CertificateWarning { get; init; } = TimeSpan.FromDays(30);
    // A worker loop is stalled when it has not progressed for this multiple of its expected period.
    public double WorkerStallFactor { get; init; } = 3;
    public TimeSpan DatabaseTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public void Validate()
    {
        var intervalsAreUsable = SnapshotInterval > TimeSpan.Zero && SnapshotInterval <= TimeSpan.FromDays(1) && CertificateWarning > TimeSpan.Zero;
        var stallFactorIsUsable = double.IsFinite(WorkerStallFactor) && WorkerStallFactor >= 1;
        var databaseTimeoutIsUsable = DatabaseTimeout > TimeSpan.Zero && DatabaseTimeout <= TimeSpan.FromMinutes(5);
        if (!intervalsAreUsable || !stallFactorIsUsable || !databaseTimeoutIsUsable)
        {
            throw new InvalidOperationException(
                "Diagnostics intervals must be positive (the snapshot at most a day), the stall factor a number of at least 1 and the database timeout at most 5 minutes.");
        }
    }
}
