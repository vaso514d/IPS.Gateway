namespace IPS.Middleware.Application.Inbound.Composition;

public enum IncomingCompositionStatus
{
    Terminal,
    Held,
    Deferred,
    OwnershipLost,
    ReplyReady
}

public sealed class IncomingCompositionResult
{
    public IncomingCompositionResult(IncomingCompositionStatus status, Guid? paymentId = null)
    {
        Status = status;
        PaymentId = paymentId;
    }

    public IncomingCompositionStatus Status { get; init; }
    public Guid? PaymentId { get; init; }
}
