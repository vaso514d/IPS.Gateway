using IPS.Middleware.Domain;

namespace IPS.Middleware.Infrastructure.Persistence.Events;

/// <summary>Persistence-only identity shared by all aggregates. State rows and events reference it with a matching kind.</summary>
internal sealed class AggregateIdentity
{
    internal const string KindColumn = "AggregateKind";
    internal const int KindLength = 32;

    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;

    public static AggregateIdentity For(AggregateRoot aggregate) => new() { Id = aggregate.Id, Kind = EventRegistry.Kind(aggregate) };
}
