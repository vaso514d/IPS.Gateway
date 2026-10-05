using System.Data.Common;
using System.Data.SqlTypes;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Inbound;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Repositories.Inbound;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using IPS.Middleware.Infrastructure.Transactions;
using IPS.Middleware.Infrastructure.UnitOfWork;
using IPS.Middleware.IntegrationTests.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

public sealed class InboundJournalTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task Concurrent_duplicates_preserve_the_original_receipt_and_count_every_delivery()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var provider = Provider(database);
        var register = provider.GetRequiredService<InboundReceiptRegistration>();
        var queue = provider.GetRequiredService<InboundProcessingChannel>();
        var first = await register.RegisterAsync(Receipt(1, xml: " \r\n<original/> "), default);
        Assert.True(queue.TryRead(out _));
        var duplicates = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            register.RegisterAsync(new(" itsbge22 ", 1, "pacs.009", "changed", i % 2 == 0, Now.AddSeconds(i)), default)));
        Assert.All(duplicates, result => { Assert.False(result.Created); Assert.Equal(first.JournalId, result.JournalId); });
        Assert.False(queue.TryRead(out _));
        await using var read = database.Context();
        var stored = (await new InboundReceiptRepository(read).ReadAsync(first.JournalId, default))!;
        Assert.Equal(8, stored.DuplicateCount);
        Assert.Equal(Now.AddSeconds(7), stored.LastDuplicateAtUtc);
        Assert.Equal(" \r\n<original/> ", stored.Receipt.RawXml);
        Assert.Equal("pacs.008", stored.Receipt.MessageType);
        Assert.False(stored.Receipt.PossibleDuplicate);
        Assert.Equal(Now, stored.Receipt.ReceivedAtUtc);
        Assert.Equal(Now, stored.NextActionAtUtc);
        Assert.Equal(InboundProcessingStatus.Pending, stored.Status);
    }

    [Fact]
    public async Task Insert_race_has_one_winner_and_sequence_is_scoped_to_participant()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var provider = Provider(database);
        var register = provider.GetRequiredService<InboundReceiptRegistration>();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => register.RegisterAsync(Receipt(9), default)));
        Assert.Single(results.Where(r => r.Created));
        Assert.Single(results.Select(r => r.JournalId).Distinct());
        var other = await register.RegisterAsync(new("OTHERBIC", 9, "pacs.008", "<other/>", true, Now), default);
        Assert.True(other.Created);
        Assert.NotEqual(results[0].JournalId, other.JournalId);
        await using var read = database.Context();
        Assert.Equal(7, (await new InboundReceiptRepository(read).ReadAsync(results[0].JournalId, default))!.DuplicateCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Invalid_sequences_are_durable_held_and_never_queued(long? sequence)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var provider = Provider(database);
        var register = provider.GetRequiredService<InboundReceiptRegistration>();
        var first = await register.RegisterAsync(Receipt(sequence), default);
        var second = await register.RegisterAsync(Receipt(sequence), default);
        Assert.True(first.Created);
        Assert.NotEqual(first.JournalId, second.JournalId);
        Assert.False(provider.GetRequiredService<InboundProcessingChannel>().TryRead(out _));
        await using var read = database.Context();
        var row = (await new InboundReceiptRepository(read).ReadAsync(first.JournalId, default))!;
        Assert.Equal(sequence, row.Receipt.Sequence);
        Assert.Equal(InboundProcessingStatus.Held, row.Status);
        Assert.NotNull(row.HoldReason);
        Assert.Null(row.NextActionAtUtc);
        Assert.Empty(await new InboundWorkRepository(read).FindDueAsync(Now, 10, default));
        Assert.Null(await Work(read).AcquireAsync(first.JournalId, Lease, default));
    }

    [Fact]
    public async Task Competing_claims_are_fenced_and_expired_owners_cannot_complete()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await Insert(database, Receipt(1));
        await using var left = database.Context();
        await using var right = database.Context();
        var first = (await new InboundWorkRepository(left).StageClaimAsync(id, Now, Lease, default))!;
        var second = (await new InboundWorkRepository(right).StageClaimAsync(id, Now, Lease, default))!;
        await new UnitOfWork(left).SaveAsync();
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => new UnitOfWork(right).SaveAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new UnitOfWork(right).SaveAsync());
        await using (var live = database.Context())
        {
            Assert.Null(await Work(live).AcquireAsync(id, Lease, default));
        }

        await using (var expired = database.Context())
        {
            Assert.False(await Work(expired, Now + Lease).CompleteAsync(first, default));
        }

        await using var recovery = database.Context();
        var replacement = await Work(recovery, Now + Lease).AcquireAsync(id, Lease, default);
        Assert.NotNull(replacement);
        Assert.NotEqual(first.Token, replacement.Token);
        await using (var stale = database.Context())
        {
            Assert.False(await Work(stale, Now + Lease).CompleteAsync(first, default));
        }

        Assert.True(await Work(recovery, Now + Lease).CompleteAsync(replacement, default));
        await using var read = database.Context();
        Assert.Equal(InboundProcessingStatus.Processed, (await new InboundReceiptRepository(read).ReadAsync(id, default))!.Status);
        Assert.Null(await Work(read, Now + Lease).AcquireAsync(id, Lease, default));
        Assert.NotEqual(first.Token, second.Token);
    }

    [Fact]
    public async Task Rowversion_rejects_completion_loaded_before_a_concurrent_duplicate()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await Insert(database, Receipt(1));
        await using var owner = database.Context();
        var claim = (await Work(owner).AcquireAsync(id, Lease, default))!;
        await using (var duplicate = database.Context())
        {
            await Intake(duplicate).RegisterAsync(Receipt(1), default);
        }

        Assert.False(await Work(owner).CompleteAsync(claim, default));
        await using var read = database.Context();
        var row = (await new InboundReceiptRepository(read).ReadAsync(id, default))!;
        Assert.Equal(InboundProcessingStatus.Pending, row.Status);
        Assert.Equal(1, row.DuplicateCount);
    }

    [Fact]
    public async Task Discovery_orders_due_work_and_excludes_live_held_completed_and_future_entries()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var late = await Insert(database, Receipt(1, at: Now.AddSeconds(-1)));
        var early = await Insert(database, Receipt(2, at: Now.AddSeconds(-2)));
        var live = await Insert(database, Receipt(3));
        var completed = await Insert(database, Receipt(4));
        await Insert(database, Receipt(null));
        var future = await Insert(database, Receipt(5));
        await using (var db = database.Context())
        {
            Assert.NotNull(await Work(db).AcquireAsync(live, Lease, default));
        }

        await using (var db = database.Context())
        {
            var claim = (await Work(db).AcquireAsync(completed, Lease, default))!;
            Assert.True(await Work(db).CompleteAsync(claim, default));
        }
        await using (var db = database.Context())
        {
            var claim = (await Work(db).AcquireAsync(future, Lease, default))!;
            Assert.True(await Work(db).ReleaseAsync(claim, Now.AddMinutes(2), default));
        }
        await using var read = database.Context();
        var repository = new InboundWorkRepository(read);
        Assert.Equal(new[] { early, late }, await repository.FindDueAsync(Now, 10, default));
        Assert.Equal(new[] { early }, await repository.FindDueAsync(Now, 1, default));
        Assert.Contains(live, await repository.FindDueAsync(Now + Lease, 10, default));
        Assert.Contains(future, await repository.FindDueAsync(Now.AddMinutes(2), 10, default));
    }

    [Fact]
    public async Task Queue_saturation_and_a_crash_between_commit_and_notification_are_recovered_from_sql()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var provider = Provider(database, new(1, 1));
        var register = provider.GetRequiredService<InboundReceiptRegistration>();
        var first = await register.RegisterAsync(Receipt(1, at: Now.AddSeconds(-2)), default);
        var second = await register.RegisterAsync(Receipt(2, at: Now.AddSeconds(-1)), default);
        var third = await Insert(database, Receipt(3)); // Durable intake, then a crash before notification.
        var queue = provider.GetRequiredService<InboundProcessingChannel>();
        Assert.True(queue.TryRead(out var queued));
        Assert.Equal(first.JournalId, queued);
        Assert.False(queue.TryRead(out _));
        await using var restarted = Provider(database, new(1, 1));
        var recoveryQueue = restarted.GetRequiredService<InboundProcessingChannel>();
        var discovery = restarted.GetRequiredService<InboundWorkDiscovery>();
        foreach (var expected in new[] { first.JournalId, second.JournalId, third })
        {
            Assert.Equal(1, await discovery.RefillAsync(default));
            Assert.True(recoveryQueue.TryRead(out var id));
            Assert.Equal(expected, id);
            await using var context = database.Context();
            var claim = (await Work(context).AcquireAsync(id, Lease, default))!;
            Assert.True(await Work(context).CompleteAsync(claim, default));
        }
        Assert.Equal(0, await discovery.RefillAsync(default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_unit_of_work_commits_or_rolls_back_receipt_payment_and_events_together(bool failEvents)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var db = failEvents ? database.Context(new FailEventSave()) : database.Context();
        var receipt = await new InboundReceiptRepository(db).StageRegistrationAsync(Receipt(1), default);
        var payment = OutgoingPayment.Receive(Guid.NewGuid(), "pacs.009", "mixed-save", Now);
        new OutgoingPaymentRepository(db).Add(payment, "{}", null);
        var save = new UnitOfWork(db);
        if (failEvents)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => save.SaveAsync());
        }
        else
        {
            await save.SaveAsync();
        }

        await using var read = database.Context();
        Assert.Equal(!failEvents, await new InboundReceiptRepository(read).ReadAsync(receipt.JournalId, default) is not null);
        Assert.Equal(failEvents ? 0 : 1, await read.Set<OutgoingPayment>().CountAsync());
        Assert.Equal(failEvents ? 0 : 1, (await new OutgoingPaymentRepository(read).ReadEventsAsync(payment.Id, default)).Count);
    }

    [Fact]
    public async Task Cancelled_or_failed_registration_never_publishes_or_commits_receipts()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var failure = new FailEventSave();
        await using var provider = Provider(database, interceptor: failure);
        var register = provider.GetRequiredService<InboundReceiptRegistration>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => register.RegisterAsync(Receipt(1), default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => register.RegisterAsync(Receipt(2), cancelled.Token));
        Assert.False(provider.GetRequiredService<InboundProcessingChannel>().TryRead(out _));
        await using var read = database.Context();
        Assert.Empty(await new InboundWorkRepository(read).FindDueAsync(Now, 100, default));
    }

    [Theory]
    [InlineData("RawXml", "<replacement/>")]
    [InlineData("MessageType", "pacs.009")]
    public async Task Original_receipt_fields_cannot_be_modified(string property, string replacement)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await Insert(database, Receipt(1));
        await using var db = database.Context();
        await new InboundWorkRepository(db).StageClaimAsync(id, Now, Lease, default);
        var entry = Assert.Single(db.ChangeTracker.Entries());
        entry.Property(property).CurrentValue = replacement;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new UnitOfWork(db).SaveAsync());
        await using var read = database.Context();
        Assert.Equal("<original/>", (await new InboundReceiptRepository(read).ReadAsync(id, default))!.Receipt.RawXml);
    }

    [Fact]
    public async Task Cancellation_after_commit_leaves_receipt_recoverable_without_notification()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        await using var provider = Provider(database, interceptor: new CancelAfterCommit(cancellation));
        var registered = await provider.GetRequiredService<InboundReceiptRegistration>().RegisterAsync(Receipt(1), cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(registered.Created);
        var queue = provider.GetRequiredService<InboundProcessingChannel>();
        Assert.False(queue.TryRead(out _));
        Assert.Equal(1, await provider.GetRequiredService<InboundWorkDiscovery>().RefillAsync(default));
        Assert.True(queue.TryRead(out var recovered));
        Assert.Equal(registered.JournalId, recovered);
    }

    [Fact]
    public async Task Duplicate_after_completion_does_not_reset_processing_or_notify()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var id = await Insert(database, Receipt(1));
        await using (var db = database.Context())
        {
            var claim = (await Work(db).AcquireAsync(id, Lease, default))!;
            Assert.True(await Work(db).CompleteAsync(claim, default));
        }
        await using var provider = Provider(database);
        var repeated = await provider.GetRequiredService<InboundReceiptRegistration>().RegisterAsync(Receipt(1), default);
        Assert.False(repeated.Created);
        Assert.Equal(id, repeated.JournalId);
        Assert.Equal(InboundProcessingStatus.Processed, repeated.Status);
        Assert.False(provider.GetRequiredService<InboundProcessingChannel>().TryRead(out _));
        Assert.Equal(0, await provider.GetRequiredService<InboundWorkDiscovery>().RefillAsync(default));
        await using var read = database.Context();
        var row = (await new InboundReceiptRepository(read).ReadAsync(id, default))!;
        Assert.Null(row.NextActionAtUtc);
        Assert.Equal(1, row.DuplicateCount);
    }

    [Fact]
    public async Task Equal_due_times_use_receipt_time_then_sql_guid_order()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var older = await Insert(database, Receipt(1, at: Now.AddMinutes(-1)));
        var sameTime = new[] { await Insert(database, Receipt(2)), await Insert(database, Receipt(3)) };
        foreach (var id in sameTime.Prepend(older))
        {
            await using var db = database.Context();
            var claim = (await Work(db).AcquireAsync(id, Lease, default))!;
            Assert.True(await Work(db).ReleaseAsync(claim, Now, default));
        }
        await using var read = database.Context();
        var expected = sameTime.OrderBy(id => new SqlGuid(id)).Prepend(older);
        Assert.Equal(expected, await new InboundWorkRepository(read).FindDueAsync(Now, 10, default));
    }

    private sealed class CancelAfterCommit(CancellationTokenSource cancellation) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        }
    }

    private static InboundReceipt Receipt(long? sequence, string xml = "<original/>", DateTimeOffset? at = null) =>
        new("ITSBGE22", sequence, "pacs.008", xml, false, at ?? Now);
    private static InboundReceiptIntake Intake(TransactionDbContext db) => new(new InboundReceiptRepository(db), new UnitOfWork(db));
    private static InboundWork Work(TransactionDbContext db, DateTimeOffset? now = null) =>
        new(new InboundWorkRepository(db), new UnitOfWork(db), new Clock(now ?? Now));
    private static async Task<Guid> Insert(SqlTestDatabase database, InboundReceipt receipt)
    {
        await using var db = database.Context();
        return (await Intake(db).RegisterAsync(receipt, default)).JournalId;
    }
    private static ServiceProvider Provider(SqlTestDatabase database, InboundSchedulingOptions? options = null, IInterceptor? interceptor = null)
    {
        using var db = database.Context();
        var connection = db.Database.GetConnectionString()!;
        var services = new ServiceCollection().AddSingleton<TimeProvider>(new Clock(Now)).AddPersistence(connection).AddInboundFoundations(options);
        if (interceptor is not null)
        {
            services.AddDbContext<TransactionDbContext>(builder => builder.AddInterceptors(interceptor));
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
    private sealed class FailEventSave : SaveChangesInterceptor
    {
        private int _calls;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 2)
            {
                throw new InvalidOperationException("Fail after entity writes, before commit.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
