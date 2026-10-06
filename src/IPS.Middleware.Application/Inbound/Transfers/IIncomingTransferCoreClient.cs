using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Transfers;

public interface IIncomingTransferCoreClient
{
    // The EndToEndId is the idempotency key, so sending again is safe.
    Task<CoreResponse> SubmitAsync(string participantBic, IncomingPacs009 transfer, CancellationToken cancellationToken);

    Task<CoreResponse> QueryAsync(string participantBic, string endToEndId, CancellationToken cancellationToken);
}

public interface IIncomingTransferReplyInterpreter
{
    // Unknown unless the core says ACCP or RJCT and, where it names identifiers, they are the transfer's own.
    CorePaymentResult Interpret(CoreCallCompletion completion, IncomingPacs009 transfer);
}
