using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Configurations;
using IPS.Middleware.Infrastructure.Persistence.Events;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Interceptors;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Transactions;

// Keep the CLR identity used by historical EF migrations; this context owns all persistence.
public class TransactionDbContext(DbContextOptions<TransactionDbContext> options) : DbContext(options)
{
    internal PersistenceChanges Changes { get; } = new();

    internal DbSet<OutgoingPaymentMetadata> OutgoingMetadata => Set<OutgoingPaymentMetadata>();
    internal DbSet<IncomingPaymentMetadata> IncomingMetadata => Set<IncomingPaymentMetadata>();

    internal OutgoingPaymentMetadata Metadata(OutgoingPayment payment) =>
        OutgoingMetadata.Local.Single(p => p.Id == payment.Id);

    internal IncomingPaymentMetadata Metadata(IncomingPayment payment) =>
        IncomingMetadata.Local.Single(p => p.Id == payment.Id);

    internal DbSet<OutgoingStatusDeliveryRow> OutgoingStatusDeliveries => Set<OutgoingStatusDeliveryRow>();
    internal DbSet<InvestigationRow> Investigations => Set<InvestigationRow>();
    internal DbSet<OutgoingMessageRow> OutgoingMessages => Set<OutgoingMessageRow>();
    internal DbSet<IncomingReplyRow> IncomingReplies => Set<IncomingReplyRow>();
    internal DbSet<IncomingReplyAttemptRow> IncomingReplyAttempts => Set<IncomingReplyAttemptRow>();
    internal DbSet<IncomingCoreCallRow> IncomingCoreCalls => Set<IncomingCoreCallRow>();
    internal DbSet<InboundJournalEntry> InboundJournal => Set<InboundJournalEntry>();
    internal DbSet<IncomingPayment> IncomingPayments => Set<IncomingPayment>();
    internal DbSet<OutgoingPayment> Payments => Set<OutgoingPayment>();
    internal DbSet<AggregateIdentity> AggregateIdentities => Set<AggregateIdentity>();
    internal DbSet<TransactionEventRow> Events => Set<TransactionEventRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(
            PaymentPersistenceInterceptor.Instance,
            InvestigationInterceptor.Instance,
            OutgoingJournalInterceptor.Instance,
            OutgoingStatusDeliveryInterceptor.Instance,
            InboundPersistenceInterceptor.Instance,
            DomainEventsInterceptor.Instance);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutgoingPaymentMetadataConfiguration());
        modelBuilder.ApplyConfiguration(new IncomingPaymentMetadataConfiguration());
        modelBuilder.ApplyConfiguration(new OutgoingStatusDeliveryConfiguration());
        modelBuilder.ApplyConfiguration(new OutgoingMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InvestigationConfiguration());
        modelBuilder.ApplyConfiguration(new InboundJournalConfiguration());
        modelBuilder.ApplyConfiguration(new IncomingReplyConfiguration());
        modelBuilder.ApplyConfiguration(new IncomingReplyAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new IncomingCoreCallConfiguration());
        modelBuilder.ApplyConfiguration(new IncomingPaymentConfiguration());
        modelBuilder.ApplyConfiguration(new AggregateIdentityConfiguration());
        modelBuilder.ApplyConfiguration(new OutgoingPaymentConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionEventConfiguration());
    }

    internal void RequireUsable()
    {
        if (Changes.Failed)
        {
            throw new InvalidOperationException("This unit of work failed; dispose it and load a fresh scope.");
        }
    }
}
