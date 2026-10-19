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
    bool PossibleDuplicate)
{
    private const string EmptyQueue = "EMPTY";

    // Annex D 6.4: IPS marks an empty queue with ReqSts EMPTY; the UAT also sends a placeholder body (<html/>) then.
    public bool HasMessage =>
        Transport.Body.Length > 0 && !string.Equals(RequestStatus?.Trim(), EmptyQueue, StringComparison.OrdinalIgnoreCase);
}
