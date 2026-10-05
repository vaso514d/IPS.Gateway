using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Domain.Transactions;
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
    internal DbSet<OutgoingStatusDeliveryRow> OutgoingStatusDeliveries => Set<OutgoingStatusDeliveryRow>();
    internal List<OutgoingStatusDeliveryRow> PendingOutgoingStatuses { get; } = [];
    internal Dictionary<(Guid, int), string> AuthorizedOutgoingStatuses { get; } = [];
    internal DbSet<OutgoingMessageRow> OutgoingMessages => Set<OutgoingMessageRow>();
    internal List<(OutgoingMessageRow Row, EntityState State)> PendingOutgoingMessages { get; } = [];
    internal Dictionary<Guid, string> AuthorizedOutgoingMessages { get; } = [];
    internal DbSet<IncomingReplyRow> IncomingReplies => Set<IncomingReplyRow>();
    internal DbSet<IncomingReplyAttemptRow> IncomingReplyAttempts => Set<IncomingReplyAttemptRow>();
    internal HashSet<Guid> AuthorizedReplies { get; } = [];
    internal DbSet<IncomingCoreCallRow> IncomingCoreCalls => Set<IncomingCoreCallRow>();
    internal HashSet<Guid> AuthorizedIncomingCalls { get; } = [];
    internal HashSet<Guid> AuthorizedIncomingProcessing { get; } = [];
    internal DbSet<InboundJournalEntry> InboundJournal => Set<InboundJournalEntry>();
    internal DbSet<IncomingPayment> IncomingPayments => Set<IncomingPayment>();
    internal HashSet<Guid> AuthorizedInboundWork { get; } = [];
    internal HashSet<Guid> AuthorizedIncomingPaymentWork { get; } = [];
    internal DbSet<OutgoingPayment> Payments => Set<OutgoingPayment>();
    internal DbSet<AggregateIdentity> AggregateIdentities => Set<AggregateIdentity>();
    internal DbSet<TransactionEventRow> Events => Set<TransactionEventRow>();
    internal SavePhase Phase { get; set; }
    internal bool Failed { get; set; }
    internal HashSet<Guid> AuthorizedOwnership { get; } = [];
    internal Dictionary<(Guid PaymentId, string Property), string> AuthorizedArtifacts { get; } = [];

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(PaymentPersistenceInterceptor.Instance, OutgoingJournalInterceptor.Instance, OutgoingStatusDeliveryInterceptor.Instance, InboundPersistenceInterceptor.Instance, DomainEventsInterceptor.Instance);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutgoingStatusDeliveryConfiguration());
        modelBuilder.ApplyConfiguration(new OutgoingMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboundJournalConfiguration());
        modelBuilder.ApplyConfiguration(new IncomingReplyConfiguration());
        modelBuilder.ApplyConfiguration(new IncomingReplyAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new IncomingCoreCallConfiguration());
        modelBuilder.ApplyConfiguration(new IncomingPaymentConfiguration());
        modelBuilder.ApplyConfiguration(new AggregateIdentityConfiguration());
        modelBuilder.ApplyConfiguration(new OutgoingPaymentConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionEventConfiguration());
    }

    internal void CompleteSave()
    {
        PendingOutgoingStatuses.Clear();
        AuthorizedOutgoingStatuses.Clear();
        PendingOutgoingMessages.Clear();
        AuthorizedOutgoingMessages.Clear();
        AuthorizedReplies.Clear();
        AuthorizedIncomingCalls.Clear();
        AuthorizedIncomingProcessing.Clear();
        AuthorizedInboundWork.Clear();
        AuthorizedIncomingPaymentWork.Clear();
        AuthorizedOwnership.Clear();
        AuthorizedArtifacts.Clear();
    }

    internal void RequireUsable()
    {
        if (Failed) throw new InvalidOperationException("This unit of work failed; dispose it and load a fresh scope.");
    }
}

internal enum SavePhase { Idle, Entities, Events }
