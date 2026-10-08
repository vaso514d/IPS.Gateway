using IPS.Middleware.Application.Inbound.Processing;

namespace IPS.Middleware.Application.Inbound.Reconciliation;

public interface IIncomingReversalClient
{
    Task<CoreResponse> RequestAsync(ReversalNotification notification, CancellationToken cancellationToken);
}
