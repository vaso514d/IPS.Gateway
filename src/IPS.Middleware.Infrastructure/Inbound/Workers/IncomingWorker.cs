using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IPS.Middleware.Infrastructure.Inbound.Workers;

/// <summary>Stop admitting work first, drain supervised tasks, then cancel their bounded remote/persistence attempts.</summary>
public abstract class IncomingWorker(IncomingWorkerOptions options, TimeProvider time, ILogger logger) : BackgroundService
{
    protected IncomingWorkerOptions Options { get; } = options;
    protected TimeProvider Time { get; } = time;
    protected ILogger Logger { get; } = logger;

    private readonly Lock lifecycle = new();
    private Task? stopping;
    private bool disposed;
    private readonly CancellationTokenSource admission = new();
    private readonly CancellationTokenSource execution = new();

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Options.Enabled)
        {
            return;
        }

        using var registration = stoppingToken.Register(() =>
        {
            admission.Cancel();
            execution.Cancel();
        });
        try
        {
            await RunAsync(admission.Token, execution.Token);
        }
        catch (OperationCanceledException) when (admission.IsCancellationRequested || execution.IsCancellationRequested) { }
    }

    protected abstract Task RunAsync(CancellationToken stop, CancellationToken work);

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            if (disposed)
            {
                return stopping ?? Task.CompletedTask;
            }

            return stopping ??= DrainAsync(cancellationToken);
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        await admission.CancelAsync();
        if (ExecuteTask is not null)
        {
            using var budget = new CancellationTokenSource(Options.ShutdownBudget, Time);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, cancellationToken);
            try
            {
                await ExecuteTask.WaitAsync(deadline.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                await execution.CancelAsync();
            }
        }
        // Existing workflows persist observed replies with their own short budget after cancellation.
        await base.StopAsync(CancellationToken.None);
    }

    protected async Task ObserveAsync(Guid id, Func<Guid, CancellationToken, Task> process, CancellationToken work)
    {
        try
        {
            await process(id, work);
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested) { }
        catch (Exception error)
        {
            Logger.LogError(error, "Incoming work {WorkId} failed; SQL recovery will rediscover it", id);
        }
    }

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

    protected async Task RefillAsync(Func<CancellationToken, Task> refill, TimeSpan interval, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await refill(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                Logger.LogError(error, "Incoming discovery failed; the next sweep will retry");
            }
            await Task.Delay(interval, Time, stop);
        }
    }

    public override void Dispose()
    {
        lock (lifecycle)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            base.Dispose();
            admission.Dispose();
            execution.Dispose();
        }
    }
}
