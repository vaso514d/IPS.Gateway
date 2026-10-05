using IPS.Middleware.Application.Inbound.Receipts;

namespace IPS.Middleware.Application.Inbound.Composition;

public sealed class IncomingReceiptState
{
    public IncomingReceiptState(
        InboundProcessingStatus status,
        DateTimeOffset? nextActionAtUtc,
        Guid? paymentId,
        bool hasReply,
        bool hasDecision)
    {
        Status = status;
        NextActionAtUtc = nextActionAtUtc;
        PaymentId = paymentId;
        HasReply = hasReply;
        HasDecision = hasDecision;
    }

    public InboundProcessingStatus Status { get; init; }
    public DateTimeOffset? NextActionAtUtc { get; init; }
    public Guid? PaymentId { get; init; }
    public bool HasReply { get; init; }
    public bool HasDecision { get; init; }
}
