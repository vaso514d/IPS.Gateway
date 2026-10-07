using System.Collections.Concurrent;
using IPS.Middleware.Application.Diagnostics;
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
    private readonly ConcurrentDictionary<string, LoopBeat> _beats = new();
    private Task? _stopping;
    private bool _disposed;

    protected TimeProvider Time { get; } = time;
    protected ILogger Logger { get; } = logger;
    protected bool IsAdmitting => !_admission.IsCancellationRequested;
    protected CancellationToken ExecutionToken => _execution.Token;

    // The service is healthy while every loop has progressed within stallFactor times the period it promised, plus the time one
    // pass of the loop may take (a sweep waits on SQL, so a short period alone would flag a busy but healthy loop).
    // The three reads are not taken together, so a probe made while the service stops may still say Running; the next one says Draining.
    public WorkerHealth Health(DateTimeOffset now, double stallFactor, TimeSpan passAllowance)
    {
        var name = GetType().Name;
        if (!enabled)
        {
            return new(name, WorkerState.Disabled, null);
        }

        if (_stopping is not null)
        {
            return new(name, WorkerState.Draining, "The service is stopping.");
        }

        if (ExecuteTask is not { } running)
        {
            return new(name, WorkerState.NotStarted, "The service has not started.");
        }

        if (running.IsFaulted)
        {
            return new(name, WorkerState.Faulted, running.Exception?.GetBaseException().Message);
        }

        if (running.IsCompleted)
        {
            return new(name, WorkerState.Stopped, "The service has stopped.");
        }

        var stalled = _beats.Where(beat => now - beat.Value.AtUtc > beat.Value.Period * stallFactor + passAllowance).Select(beat => beat.Key).Order().ToArray();
        return stalled.Length == 0
            ? new(name, WorkerState.Running, null)
            : new(name, WorkerState.Stalled, "No progress in: " + string.Join(", ", stalled));
    }

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

    // A loop reports that it is alive and when it promises to report again.
    protected void Beat(string loop, TimeSpan period) => _beats[loop] = new(Time.GetUtcNow(), period);

    // Work admitted outside RunAsync; the returned task completes when that work has drained.
    protected virtual Task StopAdmissionAsync() => Task.CompletedTask;

    protected async Task RunSweepsAsync(string sweep, Func<CancellationToken, Task> runSweep, TimeSpan interval, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            Beat(sweep, interval);
            try
            {
                await runSweep(stop);
                // A single pass that outlasts stallFactor times its interval reads as stalled; the next beat ends that.
                Beat(sweep, interval);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                Logger.LogError(error, "{Sweep} failed; the next sweep will retry", sweep);
                PaymentMetrics.ErrorLogged(GetType().Name);
            }

            await Task.Delay(interval, Time, stop);
        }
    }

    private readonly record struct LoopBeat(DateTimeOffset AtUtc, TimeSpan Period);

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
