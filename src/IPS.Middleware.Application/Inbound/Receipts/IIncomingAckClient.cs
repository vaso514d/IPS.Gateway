using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Inbound.Receipts;

// Tells IPS a message with this sequence is safely stored, so it stops redelivering it.
public interface IIncomingAckClient
{
    Task<IpsSubmissionResponse> AcknowledgeAsync(string participantBic, long sequence, CancellationToken cancellationToken);
}
