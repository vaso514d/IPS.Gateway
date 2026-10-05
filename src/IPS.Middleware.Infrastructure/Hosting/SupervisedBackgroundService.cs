using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IPS.Middleware.Infrastructure.Hosting;

// Shutdown stops admitting work first, drains running work within the shutdown budget, then cancels its bounded attempts.
public abstract class SupervisedBackgroundService(bool enabled, TimeSpan shutdownBudget, TimeProvider time, ILogger logger)
    : BackgroundService
{
    private readonly CancellationTokenSource _admission = new();
    private readonly CancellationTokenSource _execution = new();
    private readonly Lock _lifecycle = new();
    private Task? _stopping;
    private bool _disposed;

    protected TimeProvider Time { get; } = time;
    protected ILogger Logger { get; } = logger;
    protected bool IsAdmitting => !_admission.IsCancellationRequested;
    protected CancellationToken ExecutionToken => _execution.Token;

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycle)
        {
            if (_disposed)
            {
                return _stopping ?? Task.CompletedTask;
            }

            return _stopping ??= DrainAsync(cancellationToken);
        }
    }

    public override void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _admission.Cancel();
            _execution.Cancel();
            base.Dispose();
            _admission.Dispose();
            _execution.Dispose();
        }
    }

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!enabled)
        {
            return;
        }

        using var registration = stoppingToken.Register(() =>
        {
            _admission.Cancel();
            _execution.Cancel();
        });
        try
        {
            await RunAsync(_admission.Token, _execution.Token);
        }
        catch (OperationCanceledException) when (_admission.IsCancellationRequested || _execution.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    // stop ends admission of new work; work cancels the bounded attempts already running.
    protected abstract Task RunAsync(CancellationToken stop, CancellationToken work);

    // Work admitted outside RunAsync; the returned task completes when that work has drained.
    protected virtual Task StopAdmissionAsync() => Task.CompletedTask;

    protected async Task RunSweepsAsync(string sweep, Func<CancellationToken, Task> runSweep, TimeSpan interval, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await runSweep(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                Logger.LogError(error, "{Sweep} failed; the next sweep will retry", sweep);
            }

            await Task.Delay(interval, Time, stop);
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        await _admission.CancelAsync();
        var drained = Task.WhenAll(StopAdmissionAsync(), ExecuteTask ?? Task.CompletedTask);
        using var budget = new CancellationTokenSource(shutdownBudget, Time);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, cancellationToken);
        try
        {
            await drained.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            await _execution.CancelAsync();
        }

        // Running workflows persist observed replies with their own short budget after cancellation.
        await drained;
        await base.StopAsync(CancellationToken.None);
    }
}
