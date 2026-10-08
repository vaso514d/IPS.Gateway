using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Payments.Investigation;

// One resend with its journaled exchange and result. A pacs.008 resend is authorized by a NotFound investigation
// (InvestigationId); a resend of any other message type is a possible-duplicate attempt with its own frozen deadline.
public sealed record ResendAttempt(
    Guid Id,
    int Number,
    Guid? InvestigationId,
    DateTimeOffset? DeadlineUtc,
    OutgoingMessage? Request,
    OutgoingMessage? Response,
    IpsReply? Result,
    string? TransportFailure);
