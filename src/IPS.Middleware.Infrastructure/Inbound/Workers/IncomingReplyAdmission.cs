namespace IPS.Middleware.Infrastructure.Inbound.Workers;

// Shared by immediate replies and recovery; acquire before a receipt claim, not after a send marker.
public sealed class IncomingReplyAdmission(int capacity) : IDisposable
{
    private readonly SemaphoreSlim slots = new(capacity, capacity);

    public async Task RunAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        await slots.WaitAsync(token);
        try
        {
            await action(token);
        }
        finally
        {
            slots.Release();
        }
    }

    public void Dispose() => slots.Dispose();
}
