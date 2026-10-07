using System.Collections.Concurrent;

namespace IPS.Middleware.Simulators;

// What the simulated IPS, CBS and Proxy Solution were asked and how they are told to answer. Test support only.
public sealed class SimulatorState
{
    private int _lostReplies;
    private int _unresolvedReplies;
    private TaskCompletionSource _release = NewRelease();

    public ConcurrentQueue<ReceivedMessage> Messages { get; } = new();
    public ConcurrentQueue<string> Callbacks { get; } = new();
    public ConcurrentQueue<ReceivedProxyCall> ProxyCalls { get; } = new();

    // The next IPS answers are RJCT instead of ACCP.
    public bool Reject { get; set; }
    // The next proxy answers are rejects instead of accepts.
    public bool RejectProxy { get; set; }
    // Every IPS answer waits this long first.
    public TimeSpan Delay { get; set; }
    // Every IPS answer waits until Release is called.
    public bool Block { get; set; }

    public void LoseNext(int replies) => _lostReplies = replies;

    public void AnswerUnresolved(int replies) => _unresolvedReplies = replies;

    public bool TakeLostReply() => Interlocked.Decrement(ref _lostReplies) >= 0;

    public bool TakeUnresolvedReply() => Interlocked.Decrement(ref _unresolvedReplies) >= 0;

    public Task WaitForReleaseAsync(CancellationToken cancellationToken) => _release.Task.WaitAsync(cancellationToken);

    public void Release() => _release.TrySetResult();

    public void Reset()
    {
        Messages.Clear();
        Callbacks.Clear();
        ProxyCalls.Clear();
        Reject = false;
        RejectProxy = false;
        Delay = TimeSpan.Zero;
        Block = false;
        _lostReplies = 0;
        _unresolvedReplies = 0;
        _release.TrySetResult();
        _release = NewRelease();
    }

    private static TaskCompletionSource NewRelease() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed record ReceivedMessage(string Xml, bool PossibleDuplicate, string? Version, string? Channel, DateTimeOffset ReceivedAtUtc);

public sealed record ReceivedProxyCall(string Operation, string Xml, string? Channel, DateTimeOffset ReceivedAtUtc);

// What the control API accepts; every member is optional.
public sealed record Behaviour(bool? Reject, bool? RejectProxy, int? DelayMilliseconds, bool? Block, int? LoseNext, int? AnswerUnresolved);
