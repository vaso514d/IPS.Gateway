namespace IPS.Middleware.Application.Inbound.Composition;

public sealed class IncomingCompositionOptions
{
    public IncomingCompositionOptions(TimeSpan? continuationDelay = null)
    {
        ContinuationDelay = continuationDelay ?? TimeSpan.FromSeconds(1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ContinuationDelay, TimeSpan.Zero);
    }

    public TimeSpan ContinuationDelay { get; }
}
