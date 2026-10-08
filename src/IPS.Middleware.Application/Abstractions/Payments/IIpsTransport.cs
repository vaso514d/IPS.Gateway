using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Abstractions.Payments;

public interface IIpsTransport
{
    // Send a prepared message and return the complete HTTP response; failures leave the outcome unknown.
    Task<IpsSubmissionResponse> SendAsync(string xml, CancellationToken cancellationToken);

    // Send the same message again after an unknown outcome, flagged as a possible duplicate so IPS answers with the
    // original's status instead of rejecting the repeat.
    Task<IpsSubmissionResponse> ResendAsync(string xml, CancellationToken cancellationToken);
}
