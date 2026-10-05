using IPS.Middleware.Application.Inbound.Receipts;

namespace IPS.Middleware.Application.Inbound.Composition;

public sealed record IncomingReceiptState(InboundProcessingStatus Status, DateTimeOffset? NextActionAtUtc,
    Guid? PaymentId, bool HasReply, bool HasDecision);
