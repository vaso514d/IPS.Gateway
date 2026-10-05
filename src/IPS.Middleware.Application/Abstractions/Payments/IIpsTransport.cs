using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Abstractions.Payments;

public interface IIpsTransport
{
    // Send a prepared message and return the complete HTTP response; failures leave the outcome unknown.
    Task<IpsSubmissionResponse> SendAsync(string xml, CancellationToken cancellationToken);
}
