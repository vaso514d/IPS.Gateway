using IPS.Middleware.Application.Payments.Execution;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Domain.Transactions;
using Xunit;

namespace IPS.Middleware.Tests.Payments;

public sealed class OutgoingSubmissionTests
{
    [Fact]
    public async Task Slow_sql_observation_is_cancelled_at_http_deadline_and_returns_committed_intake()
    {
        var execution = new Execution();
        var workflow = new OutgoingSubmission(execution, new(httpWait: TimeSpan.FromMilliseconds(100)), TimeProvider.System);
        var result = await workflow.SubmitAsync(new(), "{}", default).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(result.TimedOut);
        Assert.Equal(execution.Initial, result.Status);
        Assert.Equal(1, execution.Started);
        Assert.True(execution.ReadCancelled);
    }
    [Fact]
    public async Task Caller_cancellation_stops_observation_without_changing_admitted_attempt()
    {
        var execution = new Execution();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var workflow = new OutgoingSubmission(execution, new(), TimeProvider.System);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow.SubmitAsync(new(), "{}", stop.Token));
        Assert.Equal(1, execution.Started);
    }
    [Fact]
    public async Task Duplicate_returns_snapshot_without_admission_or_another_read()
    {
        var execution = new Execution { Created = false };
        var result = await new OutgoingSubmission(execution, new(), TimeProvider.System).SubmitAsync(new(), "{}", default);
        Assert.False(result.TimedOut);
        Assert.Equal(execution.Initial, result.Status);
        Assert.Equal(0, execution.Started);
        Assert.Equal(0, execution.Reads);
    }
    private sealed class Execution : IOutgoingExecution
    {
        public bool Created { get; init; } = true;
        public int Started { get; private set; }
        public int Reads { get; private set; }
        public bool ReadCancelled { get; private set; }
        public OutgoingStatus Initial { get; } = new(Guid.NewGuid(), 1, "pacs.008", "ref", TransactionStatus.Received, DateTimeOffset.UtcNow, new(), "msg", "e2e");
        public Task<OutgoingAcceptance> AcceptAsync(Pacs008Request request, string json, CancellationToken token) =>
            Task.FromResult(new OutgoingAcceptance(new(Initial, Created, DateTimeOffset.UtcNow), []));
        public bool TryStart(Guid id)
        {
            Started++;
            return true;
        }
        public async Task<OutgoingStatus?> ReadAsync(string reference, CancellationToken token)
        {
            Reads++;
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException) { ReadCancelled = true; throw; }
            return null;
        }
    }
}
