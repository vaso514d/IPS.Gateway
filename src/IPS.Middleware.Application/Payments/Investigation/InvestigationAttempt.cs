using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Payments.Investigation;

public sealed record InvestigationIdentity(
    Guid Id,
    int Number,
    string MessageId,
    string StatusRequestId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset DeadlineUtc);

public sealed record InvestigationAttempt(
    InvestigationIdentity Identity,
    string? UnsignedXml,
    OutgoingMessage? Request,
    OutgoingMessage? Response,
    InvestigationReply? Result,
    string? TransportFailure);
