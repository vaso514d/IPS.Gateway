using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Pacs008;

public sealed record IncomingProcessingContext(Guid JournalId, DateTimeOffset ReceivedAtUtc, DateTimeOffset DeadlineUtc,
    IncomingPacs008Reference Original);
public enum CoreCallKind { Submission, Status }
public sealed record CoreHeader(string Name, string Value);
public sealed class CoreResponse(int statusCode, string body, IReadOnlyList<CoreHeader>? headers = null)
{
    public int StatusCode { get; } = statusCode;
    public string Body { get; } = body;
    public IReadOnlyList<CoreHeader> Headers { get; } = Array.AsReadOnly((headers ?? []).ToArray());
}
public sealed record CoreCallCompletion(CoreResponse? Response, string? Failure, DateTimeOffset ObservedAtUtc);
public sealed record IncomingCoreCall(Guid Id, int Number, CoreCallKind Kind, Guid OwnerToken, DateTimeOffset StartedAtUtc,
    CoreCallCompletion? Completion, bool Consumed);
public sealed record IncomingProcessingSnapshot(IncomingPayment Payment, Pacs008Request Request,
    IncomingProcessingContext Context, IReadOnlyList<IncomingCoreCall> Calls, DateTimeOffset? FollowUpAtUtc);
