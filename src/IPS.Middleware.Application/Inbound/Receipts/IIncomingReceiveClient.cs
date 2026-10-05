using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Inbound.Receipts;

public interface IIncomingReceiveClient
{
    Task<IncomingReceiveResponse> ReceiveAsync(CancellationToken cancellationToken);
}

// Raw evidence is retained even for empty polls and unsuccessful HTTP responses.
public sealed class IncomingReceiveResponse
{
    public IncomingReceiveResponse(
        string participantBic,
        DateTimeOffset receivedAtUtc,
        IpsSubmissionResponse transport,
        string? requestStatus,
        string? messageType,
        long? sequence,
        bool possibleDuplicate)
    {
        ParticipantBic = participantBic;
        ReceivedAtUtc = receivedAtUtc;
        Transport = transport;
        RequestStatus = requestStatus;
        MessageType = messageType;
        Sequence = sequence;
        PossibleDuplicate = possibleDuplicate;
    }

    public string ParticipantBic { get; init; }
    public DateTimeOffset ReceivedAtUtc { get; init; }
    public IpsSubmissionResponse Transport { get; init; }
    public string? RequestStatus { get; init; }
    public string? MessageType { get; init; }
    public long? Sequence { get; init; }
    public bool PossibleDuplicate { get; init; }
}
