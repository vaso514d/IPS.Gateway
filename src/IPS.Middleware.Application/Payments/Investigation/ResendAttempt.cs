using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Payments.Investigation;

// One resend authorized by a NotFound investigation, with its journaled exchange and result.
public sealed record ResendAttempt(
    Guid Id,
    int Number,
    Guid InvestigationId,
    OutgoingMessage? Request,
    OutgoingMessage? Response,
    IpsReply? Result,
    string? TransportFailure);
