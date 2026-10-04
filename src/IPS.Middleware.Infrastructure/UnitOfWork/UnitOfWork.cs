using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Domain;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.UnitOfWork;

public sealed class UnitOfWork(TransactionDbContext db) : IUnitOfWork
{
    public async Task<int> SaveAsync(CancellationToken cancellationToken = default)
    {
        db.RequireUsable();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!db.ChangeTracker.HasChanges()) return 0;
            var pending = db.ChangeTracker.Entries<AggregateRoot>()
                .Where(entry => entry.Entity.PendingEvents.Count > 0)
                .Select(entry => (Aggregate: entry.Entity, EventIds: entry.Entity.PendingEvents.Select(e => e.EventId).ToArray()))
                .ToArray();

            int written;
            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                db.Phase = SavePhase.Entities;
                written = await db.SaveChangesAsync(cancellationToken);
                db.Phase = SavePhase.Events;
                written += await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            foreach (var snapshot in pending) snapshot.Aggregate.AcknowledgeCommittedEvents(snapshot.EventIds);
            db.CompleteSave();
            return written;
        }
        catch (Exception exception)
        {
            db.Failed = true;
            if (Translate(exception) is { } persistence) throw persistence;
            throw;
        }
        finally
        {
            db.Phase = SavePhase.Idle;
        }
    }

    private static Exception? Translate(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException => new PersistenceConcurrencyException("Tracked changes conflict with a concurrent writer.", exception),
        DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } =>
            new UniqueConstraintException("Tracked changes violate a unique database constraint.", exception),
        _ => null
    };
}
