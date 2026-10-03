using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Configurations;
using IPS.Middleware.Infrastructure.Persistence.Events;
using IPS.Middleware.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Transactions;

// Keep the CLR identity used by historical EF migrations; this context owns all persistence.
public class TransactionDbContext(DbContextOptions<TransactionDbContext> options) : DbContext(options)
{
    internal DbSet<OutgoingPayment> Payments => Set<OutgoingPayment>();
    internal DbSet<TransactionEventRow> Events => Set<TransactionEventRow>();
    internal SavePhase Phase { get; set; }
    internal bool Failed { get; set; }
    internal HashSet<Guid> AuthorizedOwnership { get; } = [];

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(PaymentPersistenceInterceptor.Instance, DomainEventsInterceptor.Instance);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutgoingPaymentConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionEventConfiguration());
    }

    internal void CompleteSave() => AuthorizedOwnership.Clear();

    internal void RequireUsable()
    {
        if (Failed) throw new InvalidOperationException("This unit of work failed; dispose it and load a fresh scope.");
    }
}

internal enum SavePhase { Idle, Entities, Events }
