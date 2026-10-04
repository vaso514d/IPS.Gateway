using IPS.Middleware.Domain;
using IPS.Middleware.Infrastructure.Persistence.Events;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Persistence.Interceptors;

internal sealed class DomainEventsInterceptor : SaveRuleInterceptor
{
    internal static readonly DomainEventsInterceptor Instance = new();

    protected override void Apply(TransactionDbContext db)
    {
        RequireUnitOfWork(db);
        if (db.Phase == SavePhase.Entities) ValidateSequences(db);
        if (db.Phase == SavePhase.Events) AddEventRows(db);
    }

    private static void RequireUnitOfWork(TransactionDbContext db)
    {
        db.RequireUsable();
        if (db.Phase == SavePhase.Idle && db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Use the unit of work to save tracked changes.");
        if (db.ChangeTracker.Entries<TransactionEventRow>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Domain events are append-only.");
    }

    private static void ValidateSequences(TransactionDbContext db)
    {
        if (db.ChangeTracker.Entries<TransactionEventRow>().Any(e => e.State == EntityState.Added))
            throw new InvalidOperationException("Event rows are exclusively prepared by the event interceptor.");
        foreach (var entry in db.ChangeTracker.Entries<AggregateRoot>().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            var pending = entry.Entity.PendingEvents;
            // Protocol/ownership metadata may advance without a business transition or observation.
            if (entry.State == EntityState.Modified && pending.Count == 0 &&
                entry.Properties.All(p => !p.IsModified || p.Metadata.IsShadowProperty())) continue;
            var previous = entry.State == EntityState.Added ? 0 : entry.Property(p => p.EventSequence).OriginalValue;
            if (pending.Count == 0 || pending.Select(e => e.Sequence).Where((sequence, index) => sequence != previous + index + 1).Any() ||
                pending[^1].Sequence != entry.Entity.EventSequence)
                throw new InvalidOperationException("Changed aggregate state requires a contiguous set of pending events.");
        }
    }

    private static void AddEventRows(TransactionDbContext db)
    {
        var ids = db.ChangeTracker.Entries<TransactionEventRow>().Select(e => e.Entity.EventId).ToHashSet();
        foreach (var aggregate in db.ChangeTracker.Entries<AggregateRoot>().Select(e => e.Entity).ToArray())
            foreach (var occurrence in aggregate.PendingEvents)
                if (ids.Add(occurrence.EventId)) db.Events.Add(TransactionEventRow.From(occurrence));
    }
}
