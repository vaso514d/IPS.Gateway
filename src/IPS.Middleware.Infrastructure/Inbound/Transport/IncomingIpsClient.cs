using System.Globalization;
using System.Text;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Transport;

namespace IPS.Middleware.Infrastructure.Inbound.Transport;

public sealed class IncomingIpsClient(IHttpClientFactory clients, IncomingTransportSettings settings, TimeProvider time)
    : IIncomingReceiveClient, IIncomingReplyClient, IIncomingAckClient
{
    private string Channel => settings.ParticipantBic.ToUpperInvariant();

    public async Task<IncomingReceiveResponse> ReceiveAsync(CancellationToken cancellationToken)
    {
        var evidence = await SendAsync(
            IncomingHttpRegistration.IpsReceive, HttpMethod.Get, settings.MessagePath, settings.ParticipantBic, null, null, cancellationToken);
        return new IncomingReceiveResponse(
            Channel,
            time.GetUtcNow(),
            evidence,
            Header(evidence, IpsHeaders.RequestStatus),
            Header(evidence, IpsHeaders.MessageType),
            Sequence(Header(evidence, IpsHeaders.MessageSequence)),
            IsPossibleDuplicate(Header(evidence, IpsHeaders.PossibleDuplicate)));
    }

    public Task<IpsSubmissionResponse> SendAsync(string participantBic, string messageXml, CancellationToken cancellationToken) =>
        SendAsync(
            IncomingHttpRegistration.IpsReply,
            HttpMethod.Post,
            settings.MessagePath,
            participantBic,
            new StringContent(messageXml, Encoding.UTF8, "application/xml"),
            sequence: null,
            cancellationToken);

    public Task<IpsSubmissionResponse> AcknowledgeAsync(string participantBic, long sequence, CancellationToken cancellationToken) =>
        SendAsync(
            IncomingHttpRegistration.IpsReply,
            HttpMethod.Post,
            settings.AckPath,
            participantBic,
            new StringContent("", Encoding.UTF8, "text/plain"),
            sequence,
            cancellationToken);

    private async Task<IpsSubmissionResponse> SendAsync(
        string client,
        HttpMethod method,
        string path,
        string participant,
        HttpContent? content,
        long? sequence,
        CancellationToken cancellationToken)
    {
        IncomingHttpRegistration.RequireParticipant(settings, participant);
        using var request = new HttpRequestMessage(method, path.TrimStart('/')) { Content = content };
        request.Headers.Add(IpsHeaders.Channel, Channel);
        request.Headers.Add(IpsHeaders.Version, settings.IpsVersion);
        if (sequence is { } messageSequence)
        {
            request.Headers.Add(IpsHeaders.MessageSequence, messageSequence.ToString(CultureInfo.InvariantCulture));
        }

        var (status, body, headers) = await HttpEvidence.SendAsync(clients, client, request, cancellationToken);
        return new IpsSubmissionResponse(status, body, headers.Select(header => new IpsResponseHeader(header.Name, header.Value)).ToArray());
    }

    private static string? Header(IpsSubmissionResponse evidence, string name) =>
        evidence.Headers.FirstOrDefault(header => header.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static long? Sequence(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence) ? sequence : null;

    // Any present but unparseable flag is treated as a possible duplicate.
    private static bool IsPossibleDuplicate(string? value) =>
        value is not null && (!bool.TryParse(value, out var flag) || flag);
}
