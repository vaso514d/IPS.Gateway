using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace IPS.Middleware.Infrastructure.Persistence;

/// <summary>Captures repository-authorized values and defers dependent evidence until the parent fence succeeds.</summary>
internal sealed class StagedChanges<TEntity> where TEntity : class
{
    private readonly Dictionary<TEntity, PropertyValues> authorized = new(ReferenceEqualityComparer.Instance);
    private readonly List<DeferredChange> deferred = [];

    public void Authorize(EntityEntry<TEntity> entry)
    {
        var snapshot = entry.CurrentValues.Clone();
        foreach (var property in snapshot.Properties)
        {
            snapshot[property] = property.GetValueComparer().Snapshot(snapshot[property]);
        }

        authorized[entry.Entity] = snapshot;
    }

    public bool Matches(EntityEntry<TEntity> entry) =>
        authorized.TryGetValue(entry.Entity, out var expected) && expected.Properties.All(property =>
            property.GetValueComparer().Equals(expected[property], entry.CurrentValues[property]));

    public void Defer(EntityEntry<TEntity> entry)
    {
        deferred.Add(new(entry.Entity, entry.State));
        entry.State = EntityState.Detached;
    }

    public void Restore(DbContext context)
    {
        foreach (var change in deferred)
        {
            var entry = context.Entry(change.Entity);
            if (!Matches(entry))
            {
                throw new InvalidOperationException("Evidence changed after its parent was validated.");
            }

            entry.State = change.State;
        }
    }

    public void Clear()
    {
        authorized.Clear();
        deferred.Clear();
    }

    private sealed class DeferredChange(TEntity entity, EntityState state)
    {
        public TEntity Entity { get; } = entity;
        public EntityState State { get; } = state;
    }
}
