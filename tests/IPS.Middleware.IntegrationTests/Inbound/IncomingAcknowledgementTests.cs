using System.Collections.Concurrent;
using System.Diagnostics;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Hosting;
using IPS.Middleware.Infrastructure.Inbound;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Inbound.Workers;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.IntegrationTests.Diagnostics;
using IPS.Middleware.IntegrationTests.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

// 012a: the receive loop never waits for a MessageAck; acknowledgements run in their own loop after the receipt commit.
[Collection("Metrics")]
public sealed class IncomingAcknowledgementTests
{
    [Fact]
    public async Task A_slow_acknowledgement_does_not_hold_the_next_receive_calls_and_follows_the_commit()
    {
        await using var test = await Worker.StartAsync(new() { Enabled = true, AcknowledgementCapacity = 1 }, messages: 3);

        // The first acknowledgement is held far beyond the 5 seconds IPS allows between receive calls; polling goes on.
        await test.Acknowledgements.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await EventuallyAsync(() => test.Receiver.Calls >= 10);
        Assert.Equal([1L], test.Acknowledgements.Sent);

        test.Acknowledgements.Release.SetResult();
        await EventuallyAsync(() => test.Acknowledgements.Sent.Count == 3);
        Assert.Equal([1L, 2L, 3L], test.Acknowledgements.Sent.Order());
        Assert.All(test.Acknowledgements.StoredBeforeAcknowledgement, Assert.True);
    }

    [Fact]
    public async Task A_full_queue_leaves_the_acknowledgement_to_redelivery_and_keeps_polling()
    {
        using var probe = new MetricsProbe();
        await using var test = await Worker.StartAsync(new() { Enabled = true, AcknowledgementCapacity = 1, AcknowledgementBacklog = 1 }, messages: 3);

        // Sequence 1 holds the only acknowledgement slot, sequence 2 fills the queue, sequence 3 finds it full.
        await EventuallyAsync(() => test.Receiver.Calls >= 6);
        test.Acknowledgements.Release.SetResult();
        await EventuallyAsync(() => test.Acknowledgements.Sent.Count == 2);
        await Task.Delay(200);

        Assert.Equal([1L, 2L], test.Acknowledgements.Sent.Order());
        Assert.Equal(3, await test.StoredAsync());
        Assert.Equal(new Dictionary<string, long> { ["ok"] = 2, ["skipped"] = 1 }, probe.CountBy("ips.incoming.acknowledgements", "result"));
    }

    [Fact]
    public async Task Shutdown_stops_polling_then_sends_the_queued_acknowledgements()
    {
        await using var test = await Worker.StartAsync(new() { Enabled = true, AcknowledgementCapacity = 1 }, messages: 3);
        await EventuallyAsync(() => test.Receiver.Calls >= 4);

        var stopping = test.Service.StopAsync(default);
        await Task.Delay(200);
        var calls = test.Receiver.Calls;
        await Task.Delay(200);
        Assert.Equal(calls, test.Receiver.Calls);
        Assert.False(stopping.IsCompleted);
        test.Acknowledgements.Release.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([1L, 2L, 3L], test.Acknowledgements.Sent.Order());
        Assert.Equal(calls, test.Receiver.Calls);
    }

    [Fact]
    public async Task Shutdown_abandons_acknowledgements_that_outlast_the_budget_to_redelivery()
    {
        await using var test = await Worker.StartAsync(
            new() { Enabled = true, AcknowledgementCapacity = 1, ShutdownBudget = TimeSpan.FromMilliseconds(200) }, messages: 3);
        await test.Acknowledgements.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await test.Service.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([1L], test.Acknowledgements.Sent);
        Assert.True(test.Service.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_hung_acknowledgement_is_a_stall_of_the_acknowledgement_loop_only()
    {
        await using var test = await Worker.StartAsync(new() { Enabled = true, AcknowledgementCapacity = 1 }, messages: 1,
            requestTimeout: TimeSpan.FromMilliseconds(200));
        await test.Acknowledgements.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var calls = test.Receiver.Calls;
        await Task.Delay(TimeSpan.FromMilliseconds(600));

        // Receiving went on during the hang, so its own heartbeat is genuinely fresh, not merely within a long period.
        Assert.True(test.Receiver.Calls > calls);
        var health = test.Service.Health(TimeProvider.System.GetUtcNow(), stallFactor: 2, passAllowance: TimeSpan.Zero);

        Assert.Equal(WorkerState.Stalled, health.State);
        Assert.Equal("No progress in: IPS acknowledgement", health.Detail);
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "The expected worker progress was not observed.");
            await Task.Delay(20);
        }
    }

    private sealed class Worker : IAsyncDisposable
    {
        private readonly SqlTestDatabase _database;
        private readonly ServiceProvider _provider;

        private Worker(SqlTestDatabase database, ServiceProvider provider, ScriptedReceiver receiver, HeldAcknowledgements acknowledgements)
        {
            _database = database;
            _provider = provider;
            Receiver = receiver;
            Acknowledgements = acknowledgements;
            Service = provider.GetRequiredService<IncomingReceiveWorker>();
        }

        public ScriptedReceiver Receiver { get; }
        public HeldAcknowledgements Acknowledgements { get; }
        public IncomingReceiveWorker Service { get; }

        // A receive worker over real SQL whose receiver returns `messages` pacs.009 receipts and then empty polls.
        // The second receipt arrives only once the first acknowledgement has started, so the queue's contents are known.
        public static async Task<Worker> StartAsync(IncomingWorkerOptions options, int messages, TimeSpan? requestTimeout = null)
        {
            var database = await SqlTestDatabase.CreateAsync();
            await using var db = database.Context();
            var connection = db.Database.GetConnectionString()!;
            var acknowledgements = new HeldAcknowledgements(database);
            var receiver = new ScriptedReceiver(messages, acknowledgements.Entered.Task);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPersistence(connection);
            services.AddSingleton<IIncomingReceiveClient>(receiver);
            services.AddSingleton<IIncomingAckClient>(acknowledgements);
            services.AddSingleton(new IncomingWorkerOptions
            {
                Enabled = options.Enabled,
                AcknowledgementCapacity = options.AcknowledgementCapacity,
                AcknowledgementBacklog = options.AcknowledgementBacklog,
                ShutdownBudget = options.ShutdownBudget,
                EmptyDelay = TimeSpan.FromMilliseconds(10),
                ErrorDelay = TimeSpan.FromMilliseconds(10)
            });
            services.AddSingleton(new IncomingTransportSettings { Ips = new() { RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(20) } });
            services.AddInboundFoundations();
            services.AddSingleton<IncomingReceiveWorker>();
            var provider = services.BuildServiceProvider();
            var worker = new Worker(database, provider, receiver, acknowledgements);
            await worker.Service.StartAsync(default);
            return worker;
        }

        public async Task<int> StoredAsync()
        {
            await using var db = _database.Context();
            return await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM InboundMessageJournal").SingleAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Acknowledgements.Release.TrySetResult();
            await Service.StopAsync(default);
            await _provider.DisposeAsync();
            await _database.DisposeAsync();
        }
    }

    private sealed class ScriptedReceiver(int messages, Task firstAcknowledged) : IIncomingReceiveClient
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async Task<IncomingReceiveResponse> ReceiveAsync(CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call == 2)
            {
                await firstAcknowledged.WaitAsync(cancellationToken);
            }

            await Task.Yield();
            return call <= messages
                ? new("BAGAGE22", DateTimeOffset.UtcNow, new(200, $"<pacs009 n=\"{call}\"/>", []), null, PaymentMessageTypes.Pacs009, call, false)
                : new("BAGAGE22", DateTimeOffset.UtcNow, new(200, "", []), "EMPTY", null, null, false);
        }
    }

    // The first acknowledgement waits until the test releases it; each one records whether its receipt was already committed.
    private sealed class HeldAcknowledgements(SqlTestDatabase database) : IIncomingAckClient
    {
        private readonly ConcurrentQueue<long> _sent = new();
        private readonly ConcurrentQueue<bool> _stored = new();

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<long> Sent => _sent.ToArray();
        public IReadOnlyList<bool> StoredBeforeAcknowledgement => _stored.ToArray();

        public async Task<IpsSubmissionResponse> AcknowledgeAsync(string participantBic, long sequence, CancellationToken cancellationToken)
        {
            _sent.Enqueue(sequence);
            await using (var db = database.Context())
            {
                _stored.Enqueue(await db.Database.SqlQuery<int>(
                    $"SELECT COUNT(*) AS Value FROM InboundMessageJournal WHERE Sequence = {sequence}").SingleAsync(cancellationToken) == 1);
            }

            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new(200, "", []);
        }
    }
}
