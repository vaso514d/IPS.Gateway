using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Abstractions.Inbound;

public interface IIncomingCoreClient
{
    Task<CoreResponse> SubmitAsync(string participantBic, Pacs008Request payment, CancellationToken cancellationToken);
    Task<CoreResponse> QueryAsync(string participantBic, string endToEndId, CancellationToken cancellationToken);
}
