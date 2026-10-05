using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Transport;

namespace IPS.Middleware.Infrastructure.Inbound.Transport;

public sealed class IncomingIpsClient(IHttpClientFactory clients, IncomingTransportSettings settings, TimeProvider time)
    : IIncomingReceiveClient, IIncomingReplyClient
{
    public async Task<IncomingReceiveResponse> ReceiveAsync(CancellationToken cancellationToken)
    {
        var evidence = await SendAsync(IncomingHttpRegistration.IpsReceive, HttpMethod.Get, settings.ParticipantBic, null, cancellationToken);
        string? Header(string name) => evidence.Headers.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        var duplicate = Header("X-MONTRAN-IPS-PossibleDuplicate");
        return new(Channel, time.GetUtcNow(), evidence,
            Header(IpsReplyInterpreter.RequestStatusHeader), Header("X-MONTRAN-IPS-MessageType"),
            long.TryParse(Header("X-MONTRAN-IPS-MessageSeq"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence) ? sequence : null,
            duplicate is not null && (!bool.TryParse(duplicate, out var flag) || flag));
    }

    public Task<IpsSubmissionResponse> SendAsync(string participantBic, string messageXml, CancellationToken cancellationToken) =>
        SendAsync(IncomingHttpRegistration.IpsReply, HttpMethod.Post, participantBic,
            new StringContent(messageXml, Encoding.UTF8, "application/xml"), cancellationToken);

    private async Task<IpsSubmissionResponse> SendAsync(
        string client,
        HttpMethod method,
        string participant,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        IncomingHttpRegistration.RequireParticipant(settings, participant);
        using var request = new HttpRequestMessage(method, settings.MessagePath.TrimStart('/')) { Content = content };
        request.Headers.Add("X-MONTRAN-IPS-Channel", Channel);
        request.Headers.Add("X-MONTRAN-IPS-Version", settings.IpsVersion);
        var (status, body, headers) = await HttpEvidence.SendAsync(clients, client, request, cancellationToken);
        return new(status, body, headers.Select(h => new IpsResponseHeader(h.Name, h.Value)).ToArray());
    }

    private string Channel => settings.ParticipantBic.ToUpperInvariant();
}
