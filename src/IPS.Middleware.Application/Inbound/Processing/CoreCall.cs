using IPS.Middleware.Application.Inbound.Reconciliation;

namespace IPS.Middleware.Application.Inbound.Processing;

public enum CoreCallKind { Submission, Status, Reconciliation, Reversal }
public sealed record CoreHeader(string Name, string Value);
public sealed class CoreResponse(int statusCode, string body, IReadOnlyList<CoreHeader>? headers = null)
{
    public int StatusCode { get; } = statusCode;
    public string Body { get; } = body;
    public IReadOnlyList<CoreHeader> Headers { get; } = Array.AsReadOnly((headers ?? []).ToArray());
}
public sealed record CoreCallCompletion(CoreResponse? Response, string? Failure, DateTimeOffset ObservedAtUtc);
public sealed record IncomingCoreCall(Guid Id, int Number, CoreCallKind Kind, Guid OwnerToken, DateTimeOffset StartedAtUtc,
    CoreCallCompletion? Completion, bool Consumed, ReversalNotification? Notification = null);
