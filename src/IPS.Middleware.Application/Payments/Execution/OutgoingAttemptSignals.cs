using System.Collections.Concurrent;

namespace IPS.Middleware.Application.Payments.Execution;

// Wakes the request waiting on this instance for a new payment when an attempt on that payment finishes here, so it reads the
// committed outcome at once instead of at its next poll. In memory and per instance: SQL stays the record, and a payment
// another instance finishes is found by the poll. Only the request that created a payment waits for it.
public sealed class OutgoingAttemptSignals
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _waiting = new();

    internal int Waiting => _waiting.Count;

    // The runtime calls this after an attempt's last commit, whatever its outcome.
    public void Finished(Guid paymentId)
    {
        if (_waiting.TryRemove(paymentId, out var signal))
        {
            signal.SetResult();
        }
    }

    // Completes when the next attempt on the payment finishes on this instance.
    internal Task NextAsync(Guid paymentId) => _waiting
        .GetOrAdd(paymentId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
        .Task;

    internal void Forget(Guid paymentId) => _waiting.TryRemove(paymentId, out _);
}
