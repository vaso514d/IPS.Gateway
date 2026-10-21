using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Transfers;

public interface IIncomingTransferCoreClient
{
    // The transfer's key is the idempotency key, so sending again is safe. The registered transfer gives the gateway's own
    // identity and receipt time, which some deliveries carry.
    Task<CoreResponse> SubmitAsync(IncomingTransfer transfer, IIncomingTransferContent content, CancellationToken cancellationToken);

    Task<CoreResponse> QueryAsync(IncomingTransfer transfer, CancellationToken cancellationToken);
}

public interface IIncomingTransferReplyInterpreter
{
    // Unknown unless the core gives a final status and, where it names identifiers the transfer carries, they are its own.
    CorePaymentResult Interpret(CoreCallCompletion completion, IncomingTransfer transfer, IIncomingTransferContent content);
}
