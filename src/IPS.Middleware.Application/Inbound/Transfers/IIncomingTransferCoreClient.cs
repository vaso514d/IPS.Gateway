using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Transfers;

public interface IIncomingTransferCoreClient
{
    // The transfer's key is the idempotency key, so sending again is safe.
    Task<CoreResponse> SubmitAsync(string participantBic, IIncomingTransferContent transfer, CancellationToken cancellationToken);

    Task<CoreResponse> QueryAsync(string participantBic, string kind, string key, CancellationToken cancellationToken);
}

public interface IIncomingTransferReplyInterpreter
{
    // Unknown unless the core says ACCP or RJCT and, where it names identifiers the transfer carries, they are its own.
    CorePaymentResult Interpret(CoreCallCompletion completion, IIncomingTransferContent transfer);
}
