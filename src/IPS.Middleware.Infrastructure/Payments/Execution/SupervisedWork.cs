using Microsoft.Extensions.Logging;

namespace IPS.Middleware.Infrastructure.Payments.Execution;

/// <summary>Bounds and observes admitted tasks; SQL claims remain the authority across instances.</summary>
internal sealed class SupervisedWork<TKey>(int capacity, ILogger logger) where TKey : notnull
{
    private readonly Lock gate = new();
    private readonly Dictionary<TKey, Task> running = [];
    private bool stopped;
    internal bool TryStart(TKey key, Func<Task> action)
    {
        lock (gate)
        {
            if (stopped || running.Count >= capacity || running.ContainsKey(key))
            {
                return false;
            }

            running.Add(key, Task.Run(() => ObserveAsync(key, action)));
            return true;
        }
    }
    private async Task ObserveAsync(TKey key, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            logger.LogError(error, "Outgoing work {WorkId} failed; committed SQL evidence remains recoverable", key);
        }
        finally
        {
            lock (gate)
            {
                running.Remove(key);
            }
        }
    }

    internal Task StopAdmission()
    {
        lock (gate)
        {
            stopped = true;
            return Task.WhenAll(running.Values.ToArray());
        }
    }
}
