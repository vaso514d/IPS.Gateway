using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Processing;

// The core system's synchronous handling of an incoming pacs.008: the delivery carries the payment with its stored
// context (the IPS references and receipt time); the query asks about the payment by its identity.
public interface IIncomingCoreClient
{
    Task<CoreResponse> SubmitAsync(IncomingProcessingSnapshot snapshot, CancellationToken cancellationToken);
    Task<CoreResponse> QueryAsync(IncomingPayment payment, CancellationToken cancellationToken);
}
