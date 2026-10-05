using IPS.Middleware.Domain;
using IPS.Middleware.Infrastructure.Persistence.Events;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Persistence.Interceptors;

/// <summary>Prepares aggregate identity and event records; it never performs business transitions.</summary>
internal sealed class DomainEventsInterceptor : SaveRuleInterceptor
{
    internal static readonly DomainEventsInterceptor Instance = new();

    protected override void Apply(TransactionDbContext db)
    {
        RequireUnitOfWork(db);
        if (db.Changes.Phase == SavePhase.Entities)
        {
            ValidateSequences(db);
            AddIdentities(db);
        }
        if (db.Changes.Phase == SavePhase.Evidence)
        {
            AddEventRows(db);
        }
    }

    private static void RequireUnitOfWork(TransactionDbContext db)
    {
        db.RequireUsable();
        if (db.Changes.Phase == SavePhase.Idle && db.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException("Use the unit of work to save tracked changes.");
        }

        if (db.ChangeTracker.Entries<TransactionEventRow>().Any(e => e.State is EntityState.Modified or EntityState.Deleted) ||
            db.ChangeTracker.Entries<AggregateIdentity>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Domain events and aggregate identities are append-only.");
        }
    }

    private static void ValidateSequences(TransactionDbContext db)
    {
        if (db.ChangeTracker.Entries<TransactionEventRow>().Any(e => e.State == EntityState.Added) ||
            db.ChangeTracker.Entries<AggregateIdentity>().Any(e => e.State == EntityState.Added))
        {
            throw new InvalidOperationException("Event and identity rows are exclusively prepared by the event interceptor.");
        }

        foreach (var entry in db.ChangeTracker.Entries<AggregateRoot>().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            var pending = entry.Entity.PendingEvents;
            // Protocol/ownership metadata may advance without a business transition or observation.
            if (entry.State == EntityState.Modified && pending.Count == 0 &&
                entry.Properties.All(p => !p.IsModified || p.Metadata.IsShadowProperty()))
            {
                continue;
            }

            var previous = entry.State == EntityState.Added ? 0 : entry.Property(p => p.EventSequence).OriginalValue;
            if (pending.Count == 0 || pending.Select(e => e.Sequence).Where((sequence, index) => sequence != previous + index + 1).Any() ||
                pending[^1].Sequence != entry.Entity.EventSequence)
            {
                throw new InvalidOperationException("Changed aggregate state requires a contiguous set of pending events.");
            }
        }
    }

    // A new aggregate's shared identity is written with its state, before any event references it.
    private static void AddIdentities(TransactionDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries<AggregateRoot>().Where(e => e.State == EntityState.Added).ToArray())
        {
            db.AggregateIdentities.Add(AggregateIdentity.For(entry.Entity));
        }
    }

    private static void AddEventRows(TransactionDbContext db)
    {
        var ids = db.ChangeTracker.Entries<TransactionEventRow>().Select(e => e.Entity.EventId).ToHashSet();
        foreach (var aggregate in db.ChangeTracker.Entries<AggregateRoot>().Select(e => e.Entity).ToArray())
        {
            foreach (var occurrence in aggregate.PendingEvents)
            {
                if (ids.Add(occurrence.EventId))
                {
                    db.Events.Add(TransactionEventRow.From(aggregate, occurrence));
                }
            }
        }
    }
}
