using System.Diagnostics;
using IPS.Middleware.Application.Payments;
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
        var signals = new OutgoingAttemptSignals();
        var options = new OutgoingExecutionOptions(httpWait: TimeSpan.FromMilliseconds(100), statusPollInterval: TimeSpan.FromMilliseconds(10));
        var workflow = new OutgoingSubmission(execution, signals, options, TimeProvider.System);
        var result = await workflow.SubmitAsync(new Pacs008Request(), "{}", default).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(result.TimedOut);
        Assert.Equal(execution.Initial, result.Status);
        Assert.Equal(1, execution.Started);
        Assert.True(execution.ReadCancelled);
        Assert.Equal(0, signals.Waiting);
    }

    // SqlClient can report a command the deadline cancelled as a failure rather than a cancellation; it is still the deadline.
    [Fact]
    public async Task A_read_failing_because_the_http_deadline_cancelled_it_answers_the_committed_intake_as_timed_out()
    {
        var execution = new Execution { FailCancelledRead = true };
        var options = new OutgoingExecutionOptions(httpWait: TimeSpan.FromMilliseconds(100), statusPollInterval: TimeSpan.FromMilliseconds(10));
        var result = await new OutgoingSubmission(execution, new(), options, TimeProvider.System)
            .SubmitAsync(new Pacs008Request(), "{}", default)
            .WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(result.TimedOut);
        Assert.Equal(execution.Initial, result.Status);
        Assert.True(execution.ReadCancelled);
    }

    [Fact]
    public async Task Caller_cancellation_stops_observation_without_changing_admitted_attempt()
    {
        var execution = new Execution();
        var signals = new OutgoingAttemptSignals();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var workflow = new OutgoingSubmission(execution, signals, new(), TimeProvider.System);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow.SubmitAsync(new Pacs008Request(), "{}", stop.Token));
        Assert.Equal(1, execution.Started);
        Assert.Equal(0, signals.Waiting);
    }

    [Fact]
    public async Task Duplicate_returns_snapshot_without_admission_or_another_read()
    {
        var execution = new Execution { Created = false };
        var signals = new OutgoingAttemptSignals();
        var result = await new OutgoingSubmission(execution, signals, new(), TimeProvider.System).SubmitAsync(new Pacs008Request(), "{}", default);
        Assert.False(result.TimedOut);
        Assert.Equal(execution.Initial, result.Status);
        Assert.Equal(0, execution.Started);
        Assert.Equal(0, execution.Reads);
        Assert.Equal(0, signals.Waiting);
    }

    // The poll is 30 s, so only the signal of the attempt on this instance can answer within the test's few seconds.
    [Fact]
    public async Task An_attempt_finished_on_this_instance_wakes_the_waiting_request_which_reads_the_outcome_once()
    {
        var signals = new OutgoingAttemptSignals();
        var execution = new Execution { OnStart = execution => FinishLaterAsync(execution, signals) };
        var options = new OutgoingExecutionOptions(
            httpWait: TimeSpan.FromMinutes(1),
            attemptBudget: TimeSpan.FromMinutes(2),
            statusPollInterval: TimeSpan.FromSeconds(30),
            statusPollMaxInterval: TimeSpan.FromSeconds(30));
        var result = await new OutgoingSubmission(execution, signals, options, TimeProvider.System)
            .SubmitAsync(new Pacs008Request(), "{}", default)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.TimedOut);
        Assert.Equal(TransactionStatus.Accepted, result.Status!.Status);
        Assert.Equal(1, execution.Reads);
        Assert.Equal(0, signals.Waiting);
    }

    // Another instance finished the payment, so no signal comes here: the poll finds it, waiting 50, 100, 200, then 200 ms.
    [Fact]
    public async Task A_payment_finished_by_another_instance_is_found_by_the_poll_backing_off_to_its_longest_interval()
    {
        var signals = new OutgoingAttemptSignals();
        var execution = new Execution { AcceptedFromRead = 4 };
        var options = new OutgoingExecutionOptions(
            statusPollInterval: TimeSpan.FromMilliseconds(50),
            statusPollMaxInterval: TimeSpan.FromMilliseconds(200));
        var result = await new OutgoingSubmission(execution, signals, options, TimeProvider.System)
            .SubmitAsync(new Pacs008Request(), "{}", default)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TransactionStatus.Accepted, result.Status!.Status);
        Assert.Equal(4, execution.Reads);
        var waits = execution.ReadsAfter.Zip(execution.ReadsAfter.Skip(1), (first, second) => second - first).ToArray();
        // Timers can wake a little early on Windows; a 10 % margin keeps the doubling visible.
        Assert.All(waits.Zip(new[] { 100, 200, 200 }), wait => Assert.InRange(wait.First.TotalMilliseconds, wait.Second * 0.9, wait.Second * 5));
        Assert.Equal(0, signals.Waiting);
    }

    private static async Task FinishLaterAsync(Execution execution, OutgoingAttemptSignals signals)
    {
        await Task.Delay(50);
        execution.Current = execution.Initial with { Status = TransactionStatus.Accepted };
        signals.Finished(execution.Initial.PaymentId);
    }

    private sealed class Execution : IOutgoingExecution
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        public bool Created { get; init; } = true;
        public bool FailCancelledRead { get; init; }
        public int? AcceptedFromRead { get; init; }
        public Func<Execution, Task>? OnStart { get; init; }
        public int Started { get; private set; }
        public int Reads { get; private set; }
        public bool ReadCancelled { get; private set; }
        public List<TimeSpan> ReadsAfter { get; } = [];
        public OutgoingStatus Initial { get; } = new(Guid.NewGuid(), 1, "pacs.008", "ref", TransactionStatus.Received, DateTimeOffset.UtcNow, new(), "msg", "e2e");
        public OutgoingStatus? Current { get; set; }
        public Task<OutgoingAcceptance> AcceptAsync(IOutgoingPaymentRequest request, string json, CancellationToken token) =>
            Task.FromResult(new OutgoingAcceptance(new(Initial, Created, DateTimeOffset.UtcNow), []));
        public bool TryStart(Guid id)
        {
            Started++;
            _ = OnStart?.Invoke(this);
            return true;
        }
        public async Task<OutgoingStatus?> ReadAsync(string reference, CancellationToken token)
        {
            Reads++;
            ReadsAfter.Add(_clock.Elapsed);
            if (Current is { } current)
            {
                return current;
            }

            if (AcceptedFromRead is { } accepted)
            {
                return Reads >= accepted ? Initial with { Status = TransactionStatus.Accepted } : Initial;
            }

            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException) when (FailCancelledRead)
            {
                ReadCancelled = true;
                throw new InvalidOperationException("Operation cancelled by user.");
            }
            catch (OperationCanceledException) { ReadCancelled = true; throw; }
            return null;
        }
    }
}
