namespace IPS.Middleware.Application.Payments;

// A caller's request to send one outgoing payment; its client reference is the idempotency key across every message type.
public interface IOutgoingPaymentRequest
{
    string? ClientReference { get; }
}
