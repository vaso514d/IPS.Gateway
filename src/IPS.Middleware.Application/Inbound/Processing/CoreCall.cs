using IPS.Middleware.Application.Inbound.Reconciliation;

namespace IPS.Middleware.Application.Inbound.Processing;

public enum CoreCallKind
{
    Submission,
    Status,
    Reconciliation,
    Reversal
}

public sealed class CoreHeader : IEquatable<CoreHeader>
{
    public CoreHeader(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; init; }
    public string Value { get; init; }
    public bool Equals(CoreHeader? other) => other is not null && Name == other.Name && Value == other.Value;
    public override bool Equals(object? obj) => obj is CoreHeader other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Name, Value);

}

public sealed class CoreResponse(int statusCode, string body, IReadOnlyList<CoreHeader>? headers = null)
{
    public int StatusCode { get; } = statusCode;
    public string Body { get; } = body;
    public IReadOnlyList<CoreHeader> Headers { get; } = Array.AsReadOnly((headers ?? []).ToArray());
}

public sealed class CoreCallCompletion
{
    public CoreCallCompletion(CoreResponse? response, string? failure, DateTimeOffset observedAtUtc)
    {
        Response = response;
        Failure = failure;
        ObservedAtUtc = observedAtUtc;
    }

    public CoreResponse? Response { get; init; }
    public string? Failure { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed class IncomingCoreCall
{
    [System.Text.Json.Serialization.JsonConstructor]
    public IncomingCoreCall(
        Guid id,
        int number,
        CoreCallKind kind,
        Guid ownerToken,
        DateTimeOffset startedAtUtc,
        CoreCallCompletion? completion,
        bool consumed,
        ReversalNotification? notification = null)
    {
        Id = id;
        Number = number;
        Kind = kind;
        OwnerToken = ownerToken;
        StartedAtUtc = startedAtUtc;
        Completion = completion;
        Consumed = consumed;
        Notification = notification;
    }

    public Guid Id { get; init; }
    public int Number { get; init; }
    public CoreCallKind Kind { get; init; }
    public Guid OwnerToken { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public CoreCallCompletion? Completion { get; init; }
    public bool Consumed { get; init; }
    public ReversalNotification? Notification { get; init; }

    public IncomingCoreCall(IncomingCoreCall original)
    {
        Id = original.Id;
        Number = original.Number;
        Kind = original.Kind;
        OwnerToken = original.OwnerToken;
        StartedAtUtc = original.StartedAtUtc;
        Completion = original.Completion;
        Consumed = original.Consumed;
        Notification = original.Notification;
    }
}
