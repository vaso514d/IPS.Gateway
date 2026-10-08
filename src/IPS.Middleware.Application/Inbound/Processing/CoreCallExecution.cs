namespace IPS.Middleware.Application.Inbound.Processing;

// Shared remote-call policy only. Each workflow owns its checkpoints and interpretation.
internal static class CoreCallExecution
{
    internal static async Task<CoreCallCompletion> ExecuteAsync(
        Func<CancellationToken, Task<CoreResponse>> send,
        TimeSpan budget,
        TimeProvider timeProvider,
        CancellationToken serviceToken)
    {
        serviceToken.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(budget, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(serviceToken, timeout.Token);
        try
        {
            var response = await send(linked.Token).WaitAsync(linked.Token);
            return new(response, null, timeProvider.GetUtcNow());
        }
        catch (Exception error) when (!serviceToken.IsCancellationRequested)
        {
            return new(null, $"{error.GetType().Name}: {error.Message}", timeProvider.GetUtcNow());
        }
    }
}
