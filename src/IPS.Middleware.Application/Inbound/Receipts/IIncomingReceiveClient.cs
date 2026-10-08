using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Inbound.Receipts;

public interface IIncomingReceiveClient
{
    Task<IncomingReceiveResponse> ReceiveAsync(CancellationToken cancellationToken);
}

// Raw evidence is retained even for empty polls and unsuccessful HTTP responses.
public sealed record IncomingReceiveResponse(
    string ParticipantBic,
    DateTimeOffset ReceivedAtUtc,
    IpsSubmissionResponse Transport,
    string? RequestStatus,
    string? MessageType,
    long? Sequence,
    bool PossibleDuplicate);
