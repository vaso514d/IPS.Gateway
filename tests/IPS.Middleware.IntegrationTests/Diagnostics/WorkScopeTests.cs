using IPS.Middleware.Infrastructure.Inbound.Workers;
using IPS.Middleware.Infrastructure.Payments.Execution;
using Microsoft.Extensions.Logging;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Diagnostics;

public sealed class WorkScopeTests
{
    [Fact]
    public async Task A_failure_in_outgoing_work_is_logged_inside_the_scope_of_that_work_and_concurrent_work_does_not_mix()
    {
        var log = new ScopeLog();
        var work = new SupervisedWork<Guid>(2, log);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var gate = new TaskCompletionSource();

        Assert.True(work.TryStart(first, async () =>
        {
            await gate.Task;
            throw new InvalidOperationException("first failed");
        }));
        Assert.True(work.TryStart(second, async () =>
        {
            await gate.Task;
            throw new InvalidOperationException("second failed");
        }));
        gate.SetResult();
        await work.StopAdmission();

        var entries = log.Entries.OrderBy(entry => entry.Exception!.Message).ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Equal(first.ToString(), entries[0].Scope["WorkKey"]);
        Assert.Equal("outgoing", entries[0].Scope["Workflow"]);
        Assert.Equal(second.ToString(), entries[1].Scope["WorkKey"]);
    }

    [Fact]
    public async Task A_failure_in_incoming_work_is_logged_inside_the_scope_of_that_work()
    {
        var log = new ScopeLog();
        var worker = new Worker(log);
        var id = Guid.NewGuid();

        await worker.RunAsync(id, (_, _) => throw new InvalidOperationException("incoming failed"));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(("incoming", id.ToString()), (entry.Scope["Workflow"], entry.Scope["WorkKey"]));
    }

    private sealed class Worker(ILogger logger) : IncomingWorker(new IncomingWorkerOptions { Enabled = true }, TimeProvider.System, logger)
    {
        public Task RunAsync(Guid id, Func<Guid, CancellationToken, Task> process) => ObserveAsync(id, process, CancellationToken.None);

        protected override Task RunAsync(CancellationToken stop, CancellationToken work) => Task.CompletedTask;
    }
}

// Keeps what was logged together with the scopes open at that moment.
internal sealed class ScopeLog : ILogger
{
    private static readonly AsyncLocal<Stack<Dictionary<string, object?>>> Scopes = new();
    private readonly List<Entry> _entries = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<Entry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        var stack = Scopes.Value = new Stack<Dictionary<string, object?>>(Scopes.Value?.Reverse() ?? []);
        stack.Push(state is IEnumerable<KeyValuePair<string, object?>> pairs ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value) : []);
        return new Pop();
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var merged = new Dictionary<string, object?>();
        foreach (var scope in Scopes.Value?.Reverse() ?? [])
        {
            foreach (var pair in scope)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        lock (_gate)
        {
            _entries.Add(new(logLevel, formatter(state, exception), exception, merged));
        }
    }

    internal sealed record Entry(LogLevel Level, string Message, Exception? Exception, IReadOnlyDictionary<string, object?> Scope);

    private sealed class Pop : IDisposable
    {
        public void Dispose()
        {
            if (Scopes.Value is { Count: > 0 } stack)
            {
                Scopes.Value = new Stack<Dictionary<string, object?>>(stack.Skip(1).Reverse());
            }
        }
    }
}
