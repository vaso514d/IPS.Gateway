using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Domain;
using IPS.Middleware.Infrastructure.Persistence.Events;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace IPS.Middleware.Infrastructure.UnitOfWork;

public sealed class UnitOfWork(TransactionDbContext db) : IUnitOfWork
{
    public async Task<int> SaveAsync(CancellationToken cancellationToken = default)
    {
        // A failed save may leave tracked state partially accepted, so the whole scope must be discarded.
        if (db.HasFailedSave)
        {
            throw new InvalidOperationException("This unit of work failed; dispose it and load a fresh scope.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!db.ChangeTracker.HasChanges())
            {
                return 0;
            }

            return await SaveInTransactionAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            db.HasFailedSave = true;
            if (Translate(exception) is { } persistenceException)
            {
                throw persistenceException;
            }

            throw;
        }
    }

    private async Task<int> SaveInTransactionAsync(CancellationToken cancellationToken)
    {
        var aggregates = db.ChangeTracker.Entries<AggregateRoot>().ToArray();
        var pendingEvents = aggregates
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => new PendingEvents(entry.Entity, entry.Entity.PendingEvents.ToArray()))
            .ToArray();
        var statusDeliveries = OutgoingStatusOutbox.Create(db);
        var evidence = DetachEvidence();
        AddAggregateIdentities(aggregates);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var written = await db.SaveChangesAsync(cancellationToken);

        // The parent row's version check must win before dependent evidence can meet a uniqueness constraint.
        RestoreEvidence(evidence);
        AddEventRows(pendingEvents);
        db.OutgoingStatusDeliveries.AddRange(statusDeliveries);
        written += await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var pending in pendingEvents)
        {
            pending.Aggregate.AcknowledgeCommittedEvents(pending.Events.Select(occurrence => occurrence.EventId).ToArray());
        }

        return written;
    }

    // A new aggregate's shared identity is written with its state, before any event references it.
    private void AddAggregateIdentities(IEnumerable<EntityEntry<AggregateRoot>> aggregates)
    {
        foreach (var entry in aggregates.Where(entry => entry.State == EntityState.Added))
        {
            db.AggregateIdentities.Add(AggregateIdentity.For(entry.Entity));
        }
    }

    private void AddEventRows(IEnumerable<PendingEvents> pendingEvents)
    {
        foreach (var pending in pendingEvents)
        {
            foreach (var occurrence in pending.Events)
            {
                db.Events.Add(TransactionEventRow.From(pending.Aggregate, occurrence));
            }
        }
    }

    private DeferredEvidence[] DetachEvidence()
    {
        var entries = db.ChangeTracker.Entries<OutgoingMessageRow>().Cast<EntityEntry>()
            .Concat(db.ChangeTracker.Entries<InvestigationRow>())
            .Concat(db.ChangeTracker.Entries<ResendRow>())
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .ToArray();

        var deferred = entries.Select(entry => new DeferredEvidence(entry.Entity, entry.State)).ToArray();
        foreach (var entry in entries)
        {
            entry.State = EntityState.Detached;
        }

        return deferred;
    }

    private void RestoreEvidence(IEnumerable<DeferredEvidence> evidence)
    {
        foreach (var deferred in evidence)
        {
            db.Entry(deferred.Entity).State = deferred.State;
        }
    }

    private static Exception? Translate(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException =>
            new PersistenceConcurrencyException("Tracked changes conflict with a concurrent writer.", exception),
        DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } =>
            new UniqueConstraintException("Tracked changes violate a unique database constraint.", exception),
        _ => null
    };

    private sealed record PendingEvents(AggregateRoot Aggregate, IReadOnlyList<DomainEvent> Events);

    private sealed record DeferredEvidence(object Entity, EntityState State);
}
