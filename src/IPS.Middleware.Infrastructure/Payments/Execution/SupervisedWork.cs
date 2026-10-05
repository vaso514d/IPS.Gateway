using Microsoft.Extensions.Logging;

namespace IPS.Middleware.Infrastructure.Payments.Execution;

// Bounds and observes admitted tasks per key; SQL claims remain the authority across instances.
internal sealed class SupervisedWork<TKey>(int capacity, ILogger logger) where TKey : notnull
{
    private readonly Lock _gate = new();
    private readonly Dictionary<TKey, Task> _running = [];
    private bool _stopped;

    internal bool TryStart(TKey key, Func<Task> action)
    {
        lock (_gate)
        {
            if (_stopped || _running.Count >= capacity || _running.ContainsKey(key))
            {
                return false;
            }

            _running.Add(key, Task.Run(() => ObserveAsync(key, action)));
            return true;
        }
    }

    // Stops admission and returns a task that completes when the already admitted work has finished.
    internal Task StopAdmission()
    {
        lock (_gate)
        {
            _stopped = true;
            return Task.WhenAll(_running.Values.ToArray());
        }
    }

    private async Task ObserveAsync(TKey key, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // Shutdown cancelled the attempt; committed checkpoints remain recoverable.
        }
        catch (Exception error)
        {
            logger.LogError(error, "Outgoing work {WorkId} failed; committed SQL evidence remains recoverable", key);
        }
        finally
        {
            lock (_gate)
            {
                _running.Remove(key);
            }
        }
    }
}
