using System.Collections.Concurrent;
using System.Threading.Channels;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Infrastructure.Inbound;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Inbound.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

public sealed class IncomingWorkerTests
{
    [Fact]
    public async Task Disabled_worker_tolerates_repeated_stop_and_disposal()
    {
        var worker = new Dispatcher(new(new()), new(), (_, _) => Task.CompletedTask);
        await worker.StartAsync(default);
        await worker.StopAsync(default);
        worker.Dispose();
        await worker.StopAsync(default);
        worker.Dispose();
    }

    [Fact]
    public async Task Dispatcher_bounds_parallel_handlers_survives_failure_and_drains_on_shutdown()
    {
        var channel = new InboundProcessingChannel(new(capacity: 4, discoveryBatch: 4));
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        channel.TryNotify(first);
        channel.TryNotify(second);
        channel.TryNotify(third);
        var started = Channel.CreateUnbounded<Guid>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new ConcurrentQueue<Guid>();
        using var worker = new Dispatcher(channel, new()
        {
            Enabled = true
        }, async (id, token) =>
        {
            calls.Enqueue(id);
            await started.Writer.WriteAsync(id, token);
            await release.Task.WaitAsync(token);
            if (id == first)
            {
                throw new InvalidOperationException("Injected handler failure");
            }
        });
        await worker.StartAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.Equal(first, await started.Reader.ReadAsync(timeout.Token));
        Assert.Equal(second, await started.Reader.ReadAsync(timeout.Token));
        Assert.False(started.Reader.TryRead(out _));
        var stop = worker.StopAsync(default);
        Assert.False(stop.IsCompleted);
        release.SetResult();
        await stop.WaitAsync(timeout.Token);
        Assert.Equal(new[] { first, second }, calls.ToArray());
        Assert.True(channel.TryRead(out var remaining));
        Assert.Equal(third, remaining);
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Shutdown_cancels_and_awaits_handlers_after_drain_budget()
    {
        var channel = new InboundProcessingChannel(new());
        channel.TryNotify(Guid.NewGuid());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = false;
        using var worker = new Dispatcher(channel, new() { Enabled = true, ShutdownBudget = TimeSpan.FromMilliseconds(50) }, async (_, token) =>
        {
            started.SetResult();
            try
            { await Task.Delay(Timeout.Infinite, token); }
            finally { finished = true; }
        });
        await worker.StartAsync(default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(finished);
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Reply_admission_is_shared_and_cancellation_does_not_leak_slots()
    {
        using var admission = new IncomingReplyAdmission(1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = admission.RunAsync(async _ => { entered.SetResult(); await release.Task; }, default);
        await entered.Task;
        using var cancelled = new CancellationTokenSource();
        var reached = false;
        var waiting = admission.RunAsync(_ => { reached = true; return Task.CompletedTask; }, cancelled.Token);
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(reached);
        release.SetResult();
        await first;
        await admission.RunAsync(_ => { reached = true; return Task.CompletedTask; }, default);
        Assert.True(reached);
    }

    [Theory]
    [InlineData(3, 5, 2, 1, 1)]
    [InlineData(5, 5, 2, 2, 2)]
    [InlineData(100, 100, 2, 2, 97)]
    [InlineData(20, 5, 2, 2, 3)]
    public void Handler_capacity_uses_both_connection_budgets(int ips, int cbs, int followUp, int acknowledgements, int expected)
    {
        var options = new IncomingWorkerOptions { Enabled = true, CbsFollowUpCapacity = followUp, AcknowledgementCapacity = acknowledgements };
        var transport = new IncomingTransportSettings { Enabled = true, Ips = new() { ConnectionLimit = ips }, Cbs = new() { ConnectionLimit = cbs } };
        options.Validate(transport);
        Assert.Equal(expected, options.ProcessingCapacity(transport));
        Assert.Equal(ips - 1 - acknowledgements, options.IpsSendCapacity(transport));
    }

    [Fact]
    public void Invalid_limits_or_disabled_transport_cannot_enable_workers()
    {
        Assert.Throws<InvalidOperationException>(() => new IncomingWorkerOptions { Enabled = true }.Validate(new()));
        Assert.Throws<InvalidOperationException>(() => new IncomingWorkerOptions { Enabled = true, CbsFollowUpCapacity = 100 }.Validate(new() { Enabled = true }));
        Assert.Throws<InvalidOperationException>(() => new IncomingWorkerOptions { EmptyDelay = TimeSpan.Zero }.Validate(new()));
        Assert.Throws<InvalidOperationException>(() => new IncomingWorkerOptions { ShutdownBudget = TimeSpan.Zero }.Validate(new()));
        Assert.Throws<InvalidOperationException>(() => new IncomingWorkerOptions { AcknowledgementCapacity = 0 }.Validate(new()));
        Assert.Throws<InvalidOperationException>(() => new IncomingWorkerOptions { AcknowledgementBacklog = 0 }.Validate(new()));
        // Receive and two acknowledgements would leave no IPS connection for replies.
        Assert.Throws<InvalidOperationException>(() => new IncomingWorkerOptions { Enabled = true }
            .Validate(new() { Enabled = true, Ips = new() { ConnectionLimit = 3 } }));
    }

    [Fact]
    public void Follow_up_admits_a_transfer_within_two_admissions_while_reconciliation_keeps_a_backlog()
    {
        var admission = new FollowUpAdmission();
        var transfer = Guid.NewGuid();
        var admitted = new List<FollowUpKind>();

        // Capacity one: every sweep finds a fresh reconciliation backlog and the same due transfer, and one slot is free.
        for (var sweep = 0; sweep < 4; sweep++)
        {
            var transfers = admitted.Contains(FollowUpKind.Transfer) ? Array.Empty<Guid>() : [transfer];
            admitted.Add(Assert.Single(admission.Select(Fresh(5), transfers, free: 1, _ => false)).Kind);
        }

        Assert.Equal(new[] { FollowUpKind.Reconciliation, FollowUpKind.Transfer, FollowUpKind.Reconciliation, FollowUpKind.Reconciliation }, admitted);
    }

    [Fact]
    public void Follow_up_alternates_kinds_as_single_slots_free_up_and_one_kind_alone_takes_every_slot()
    {
        var admission = new FollowUpAdmission();

        var kinds = Enumerable.Range(0, 4).Select(_ => Assert.Single(admission.Select(Fresh(3), Fresh(3), free: 1, _ => false)).Kind).ToArray();
        var payments = Fresh(3);
        var paymentsOnly = admission.Select(payments, [], free: 3, _ => false);
        var transfers = Fresh(3);
        var transfersOnly = admission.Select([], transfers, free: 3, _ => false);

        Assert.Equal(new[] { FollowUpKind.Reconciliation, FollowUpKind.Transfer, FollowUpKind.Reconciliation, FollowUpKind.Transfer }, kinds);
        Assert.Equal(payments, paymentsOnly.Select(due => due.Id));
        Assert.Equal(transfers, transfersOnly.Select(due => due.Id));
    }

    [Fact]
    public void Follow_up_skips_running_work_without_using_a_slot_and_keeps_the_turn_for_the_next_item_of_its_kind()
    {
        var admission = new FollowUpAdmission();
        var running = Guid.NewGuid();
        var payment = Guid.NewGuid();
        var next = Guid.NewGuid();
        var transfer = Guid.NewGuid();

        var started = admission.Select([running, payment, next], [transfer], free: 2, id => id == running);

        Assert.Equal(new FollowUpWork[] { new(payment, FollowUpKind.Reconciliation), new(transfer, FollowUpKind.Transfer) }, started);
    }

    private static Guid[] Fresh(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();

    private sealed class Dispatcher(InboundProcessingChannel channel, IncomingWorkerOptions options, Func<Guid, CancellationToken, Task> handler)
        : IncomingWorker(options, TimeProvider.System, NullLogger.Instance)
    {
        protected override Task RunAsync(CancellationToken stop, CancellationToken work) => DispatchAsync(channel, 2, handler, stop, work);
    }
}
