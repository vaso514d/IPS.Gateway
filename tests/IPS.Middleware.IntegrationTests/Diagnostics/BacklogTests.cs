using IPS.Middleware.Application.Diagnostics;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Infrastructure.Diagnostics;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.IntegrationTests.Payments;
using IPS.Middleware.IntegrationTests.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Diagnostics;

[Collection("Metrics")]
public sealed class BacklogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_empty_database_has_nothing_due_in_any_kind()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var context = database.Context();

        var snapshot = await new BacklogReader(context, new TestClock(Now)).ReadAsync(default);

        Assert.Equal(
            new[] { "callbacks", "inbound_receipts", "incoming_payments", "incoming_transfers", "outgoing_payments" },
            snapshot.Items.Select(item => item.Kind).Order().ToArray());
        Assert.All(snapshot.Items, item => Assert.Equal((0L, 0d), (item.Count, item.OldestAgeSeconds)));
        Assert.Empty(snapshot.Journal);
    }

    [Fact]
    public async Task Due_work_is_counted_by_kind_with_the_age_of_the_oldest_and_future_work_is_not_due()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var context = database.Context())
        {
            context.InboundJournal.AddRange(
                Pending(1, Now.AddSeconds(-90)),
                Pending(2, Now.AddSeconds(-30)),
                Pending(3, Now.AddMinutes(5)),
                Processed(4),
                Held(5));
            await context.SaveChangesAsync();
        }

        await using var read = database.Context();
        var snapshot = await new BacklogReader(read, new TestClock(Now)).ReadAsync(default);

        var receipts = snapshot.Items.Single(item => item.Kind == "inbound_receipts");
        Assert.Equal(2, receipts.Count);
        Assert.Equal(90, receipts.OldestAgeSeconds);
        Assert.Equal(
            new[] { ("held", 1L), ("pending", 3L), ("processed", 1L) },
            snapshot.Journal.OrderBy(entry => entry.Status).Select(entry => (entry.Status, entry.Count)).ToArray());
    }

    [Fact]
    public async Task A_payment_awaiting_its_first_callback_is_a_due_callback_with_an_age()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptPain002Async();
        await core.ProcessAsync(id);
        core.Clock.Now += TimeSpan.FromSeconds(42);
        await using var read = core.Database.Context();

        var snapshot = await new BacklogReader(read, core.Clock).ReadAsync(default);

        var callbacks = snapshot.Items.Single(item => item.Kind == "callbacks");
        Assert.Equal(1, callbacks.Count);
        Assert.InRange(callbacks.OldestAgeSeconds, 42, 45);
    }

    [Fact]
    public async Task The_snapshot_service_publishes_what_it_read_and_the_gauges_report_it()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var context = database.Context())
        {
            context.InboundJournal.Add(Pending(1, DateTimeOffset.UtcNow.AddSeconds(-120)));
            await context.SaveChangesAsync();
        }

        using var probe = new MetricsProbe();
        using var provider = new ServiceCollection()
            .AddDbContext<Infrastructure.Transactions.TransactionDbContext>(options => options.UseSqlServer(database.Context().Database.GetConnectionString()!))
            .BuildServiceProvider();
        using var service = new BacklogSnapshotService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new DiagnosticsSettings { SnapshotInterval = TimeSpan.FromMilliseconds(100) },
            new DatabaseUse(true),
            TimeProvider.System,
            NullLogger<BacklogSnapshotService>.Instance);
        PaymentMetrics.PublishBacklog(BacklogSnapshot.Empty);

        await service.StartAsync(default);
        await WaitUntilAsync(() => PaymentMetrics.Backlog.Items.Any(item => item.Kind == "inbound_receipts" && item.Count == 1));
        await service.StopAsync(default);
        probe.Observe();

        var due = probe.Of("ips.backlog.due").Single(measured => (string)measured.Tags["kind"]! == "inbound_receipts");
        var age = probe.Of("ips.backlog.oldest_age").Single(measured => (string)measured.Tags["kind"]! == "inbound_receipts");
        Assert.Equal(1, due.Value);
        Assert.InRange(age.Value, 119, 130);
        Assert.Equal(1, probe.Of("ips.inbound.journal").Single(measured => (string)measured.Tags["status"]! == "pending").Value);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task The_snapshot_service_does_nothing_when_the_database_is_unused_or_the_snapshot_is_off(bool snapshot, bool database)
    {
        PaymentMetrics.PublishBacklog(BacklogSnapshot.Empty);
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var service = new BacklogSnapshotService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new DiagnosticsSettings { BacklogSnapshot = snapshot, SnapshotInterval = TimeSpan.FromMilliseconds(50) },
            new DatabaseUse(database),
            TimeProvider.System,
            NullLogger<BacklogSnapshotService>.Instance);

        await service.StartAsync(default);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(default);

        Assert.Empty(PaymentMetrics.Backlog.Items);
    }

    [Fact]
    public async Task A_failing_read_keeps_the_previous_snapshot_and_counts_the_error()
    {
        using var probe = new MetricsProbe();
        var previous = new BacklogSnapshot(Now, [new("callbacks", 7, 3)], []);
        PaymentMetrics.PublishBacklog(previous);
        using var provider = new ServiceCollection()
            .AddDbContext<Infrastructure.Transactions.TransactionDbContext>(options => options.UseSqlServer(
                "Server=127.0.0.1,1;Database=Missing;User Id=u;Password=p;Connect Timeout=1;TrustServerCertificate=true;Encrypt=false"))
            .BuildServiceProvider();
        using var service = new BacklogSnapshotService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new DiagnosticsSettings { SnapshotInterval = TimeSpan.FromMilliseconds(100) },
            new DatabaseUse(true),
            TimeProvider.System,
            NullLogger<BacklogSnapshotService>.Instance);

        await service.StartAsync(default);
        await WaitUntilAsync(() => probe.Of("ips.errors").Count > 0);
        await service.StopAsync(default);

        Assert.Same(previous, PaymentMetrics.Backlog);
        Assert.Equal("BacklogSnapshotService", probe.Of("ips.errors")[0].Tags["component"]);
        PaymentMetrics.PublishBacklog(BacklogSnapshot.Empty);
    }

    private static InboundJournalEntry Pending(long sequence, DateTimeOffset next) => Entry(sequence, InboundProcessingStatus.Pending, next, null);

    private static InboundJournalEntry Processed(long sequence) => Entry(sequence, InboundProcessingStatus.Processed, null, null);

    private static InboundJournalEntry Held(long sequence) => Entry(sequence, InboundProcessingStatus.Held, null, "unsupported");

    private static InboundJournalEntry Entry(long sequence, InboundProcessingStatus status, DateTimeOffset? next, string? hold) => new()
    {
        Id = Guid.NewGuid(),
        ParticipantBic = "BAGAGE22",
        Sequence = sequence,
        MessageType = "pacs.008",
        RawXml = "<x />",
        ReceivedAtUtc = Now,
        Status = status,
        NextActionAtUtc = next,
        HoldReason = hold
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "The condition was not reached in time.");
            await Task.Delay(50);
        }
    }
}
