namespace IPS.Middleware.Application.Payments.StatusDelivery;

/// <summary>One callback exchange. The implementation must not retry automatically.</summary>
public interface IOutgoingStatusReceiver
{
    Task<int> SendAsync(OutgoingStatus status, string idempotencyKey, CancellationToken cancellationToken);
}
