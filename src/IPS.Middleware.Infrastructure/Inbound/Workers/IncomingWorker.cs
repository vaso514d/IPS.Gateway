using IPS.Middleware.Infrastructure.Hosting;
using Microsoft.Extensions.Logging;

namespace IPS.Middleware.Infrastructure.Inbound.Workers;

public abstract class IncomingWorker(IncomingWorkerOptions options, TimeProvider time, ILogger logger)
    : SupervisedBackgroundService(options.Enabled, options.ShutdownBudget, time, logger)
{
    protected IncomingWorkerOptions Options { get; } = options;

    protected async Task ObserveAsync(Guid id, Func<Guid, CancellationToken, Task> process, CancellationToken work)
    {
        try
        {
            await process(id, work);
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            // Shutdown cancelled the attempt; SQL recovery resumes it.
        }
        catch (Exception error)
        {
            Logger.LogError(error, "Incoming work {WorkId} failed; SQL recovery will rediscover it", id);
        }
    }

    // Runs at most capacity items from the channel at once and waits for all of them before returning.
    protected async Task DispatchAsync(
        InboundJournalChannel channel,
        int capacity,
        Func<Guid, CancellationToken, Task> process,
        CancellationToken stop,
        CancellationToken work)
    {
        var running = new List<Task>();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                running.RemoveAll(task => task.IsCompleted);
                if (running.Count == capacity)
                {
                    await Task.WhenAny(running).WaitAsync(stop);
                    continue;
                }

                var id = await channel.ReadAsync(stop);
                stop.ThrowIfCancellationRequested();
                running.Add(ObserveAsync(id, process, work));
            }
        }
        finally
        {
            await Task.WhenAll(running);
        }
    }

    protected Task RefillAsync(Func<CancellationToken, Task> refill, TimeSpan interval, CancellationToken stop) =>
        RunSweepsAsync("Incoming discovery", refill, interval, stop);
}
