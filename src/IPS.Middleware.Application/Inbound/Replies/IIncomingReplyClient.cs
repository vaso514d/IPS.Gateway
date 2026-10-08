using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Inbound.Replies;

public interface IIncomingReplyClient
{
    // Exactly one transport attempt. The durable workflow owns the retry budget.
    Task<IpsSubmissionResponse> SendAsync(string participantBic, string messageXml, CancellationToken cancellationToken);
}
