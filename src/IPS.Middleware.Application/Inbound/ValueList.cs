using System.Collections.ObjectModel;

namespace IPS.Middleware.Application.Inbound;

// A read-only list that compares by its ordered contents, so a record holding one compares by value.
public sealed class ValueList<T>(T[] values) : ReadOnlyCollection<T>(values)
{
    public static ValueList<T>? Copy(IReadOnlyList<T>? values) => values is null ? null : new ValueList<T>(values.ToArray());

    public override bool Equals(object? obj) => obj is ValueList<T> other && this.SequenceEqual(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in this)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}
