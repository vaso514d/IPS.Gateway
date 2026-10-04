using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using IPS.Middleware.Infrastructure.Transactions;
using IPS.Middleware.Infrastructure.UnitOfWork;
using IPS.Middleware.IntegrationTests.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Persistence;

public sealed class UnitOfWorkTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Save_persists_insert_update_and_delete_without_any_payment_or_domain_events()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var context = Context(database);
        await CreateNotesTable(context);
        var unit = new UnitOfWork(context);
        var note = new Note { Id = Guid.NewGuid(), Text = "first" };
        context.Add(note);
        Assert.Equal(1, await unit.SaveAsync());
        note.Text = "second";
        Assert.Equal(1, await unit.SaveAsync());
        Assert.Equal(0, await unit.SaveAsync());
        await using (var read = Context(database))
            Assert.Equal("second", (await read.Set<Note>().SingleAsync()).Text);
        context.Remove(note);
        Assert.Equal(1, await unit.SaveAsync());
        await using var verify = Context(database);
        Assert.Empty(await verify.Set<Note>().ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_changes_and_aggregate_events_commit_or_roll_back_together(bool failEvents)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var context = Context(database);
        await CreateNotesTable(context);
        if (failEvents)
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE TransactionEvents ADD CONSTRAINT CK_Test_NoIntake CHECK (Sequence > 1)");
        var unit = new UnitOfWork(context);
        var repository = new OutgoingPaymentRepository(context);
        var payment = OutgoingPayment.Receive(Guid.NewGuid(), "pacs.008", "shared-save", Now);
        repository.Add(payment, "{}", accepted: null);
        context.Add(new Note { Id = Guid.NewGuid(), Text = "saved with payment" });
        if (failEvents)
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => unit.SaveAsync());
            Assert.Single(payment.PendingEvents);
            await Assert.ThrowsAsync<InvalidOperationException>(() => unit.SaveAsync());
        }
        else
        {
            Assert.Equal(3, await unit.SaveAsync());
            Assert.Empty(payment.PendingEvents);
        }
        await using var read = Context(database);
        Assert.Equal(failEvents ? 0 : 1, await read.Set<Note>().CountAsync());
        Assert.Equal(failEvents ? 0 : 1, await read.Set<OutgoingPayment>().CountAsync());
        Assert.Equal(failEvents ? 0 : 1, (await new OutgoingPaymentRepository(read).ReadEventsAsync(payment.Id, default)).Count);
    }

    [Fact]
    public async Task A_stale_aggregate_rolls_back_other_tracked_changes()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var stale = Context(database);
        await CreateNotesTable(stale);
        var repository = new OutgoingPaymentRepository(stale);
        var unit = new UnitOfWork(stale);
        var payment = (await new OutgoingTransactionIntake(repository, unit, TimeProvider.System)
            .AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "stale-shared-save", "{}").Request!, default)).Payment;
        await using (var winner = Context(database))
        {
            var current = await winner.Set<OutgoingPayment>().SingleAsync();
            current.RecordStep(ProcessingStep.Validated, Now);
            await new UnitOfWork(winner).SaveAsync();
        }
        payment.RecordStep(ProcessingStep.Signed, Now);
        stale.Add(new Note { Id = Guid.NewGuid(), Text = "must roll back" });
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => unit.SaveAsync());
        Assert.Single(payment.PendingEvents);
        await using var read = Context(database);
        Assert.Empty(await read.Set<Note>().ToListAsync());
        Assert.Equal(2, (await new OutgoingPaymentRepository(read).ReadEventsAsync(payment.Id, default)).Count);
    }

    [Fact]
    public async Task Uniqueness_failures_are_general_persistence_errors()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var context = Context(database);
        await CreateNotesTable(context);
        var id = Guid.NewGuid();
        context.Add(new Note { Id = id, Text = "original" });
        await new UnitOfWork(context).SaveAsync();
        await using var duplicate = Context(database);
        duplicate.Add(new Note { Id = id, Text = "replacement" });
        var exception = await Assert.ThrowsAsync<UniqueConstraintException>(() => new UnitOfWork(duplicate).SaveAsync());
        Assert.IsType<DbUpdateException>(exception.InnerException);
        await using var read = Context(database);
        Assert.Equal("original", (await read.Set<Note>().SingleAsync()).Text);
    }

    [Fact]
    public async Task Registered_repositories_and_unit_share_one_scoped_context()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var connection = database.Context();
        var services = new ServiceCollection().AddPersistence(connection.Database.GetConnectionString()!);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var payments = scope.ServiceProvider.GetRequiredService<IOutgoingPaymentRepository>();
        var work = scope.ServiceProvider.GetRequiredService<ITransactionWorkRepository>();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var preparation = scope.ServiceProvider.GetRequiredService<IPaymentPreparationRepository>();
        var payment = (await new OutgoingTransactionIntake(payments, unit, TimeProvider.System)
            .AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "registered", "{}").Request!, default)).Payment;
        var loaded = await payments.FindAsync(payment.Id, default);
        Assert.Same(payment, loaded);
        var claim = await new OutgoingTransactionWork(payments, work, scope.ServiceProvider.GetRequiredService<IPaymentSubmissionRepository>(), unit, TimeProvider.System)
            .TryStartAsync(payment.Id, TimeSpan.FromSeconds(45), default);
        Assert.NotNull(claim);
        preparation.StageUnsignedXml(payment, claim, "<registered/>", TimeProvider.System.GetUtcNow());
        await unit.SaveAsync();
        await using var read = database.Session();
        Assert.Equal(TransactionStatus.Sending, (await read.Payments.FindAsync(payment.Id, default))!.CurrentStatus);
        Assert.Equal("<registered/>", (await new PaymentPreparationRepository(read.Context).ReadAsync(payment.Id, default))!.UnsignedXml);
    }

    private static SharedTestContext Context(SqlTestDatabase database)
    {
        using var source = database.Context();
        return new(new DbContextOptionsBuilder<TransactionDbContext>()
            .UseSqlServer(source.Database.GetConnectionString()).Options);
    }

    // This table exists only in each disposable SQL fixture; it is not a production capability or migration.
    private static Task CreateNotesTable(SharedTestContext context) => context.Database.ExecuteSqlRawAsync(
        "CREATE TABLE TestNotes (Id uniqueidentifier NOT NULL PRIMARY KEY, Text nvarchar(100) NOT NULL)");

    private sealed class SharedTestContext(DbContextOptions<TransactionDbContext> options) : TransactionDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Note>().ToTable("TestNotes").HasKey(n => n.Id);
            modelBuilder.Entity<Note>().Property(n => n.Text).HasMaxLength(100).IsRequired();
        }
    }

    private sealed class Note
    {
        public Guid Id { get; set; }
        public string Text { get; set; } = "";
    }
}
