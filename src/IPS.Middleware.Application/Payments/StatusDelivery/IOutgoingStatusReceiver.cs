namespace IPS.Middleware.Application.Payments.StatusDelivery;

// One callback exchange. The implementation must not retry automatically.
public interface IOutgoingStatusReceiver
{
    Task<int> SendAsync(OutgoingStatus status, string idempotencyKey, CancellationToken cancellationToken);
}
