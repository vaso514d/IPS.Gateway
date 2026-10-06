using System.Text;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Transport;

namespace IPS.Middleware.Infrastructure.Payments.Transport;

public sealed class OutgoingIpsClient(IHttpClientFactory clients, OutgoingTransportSettings settings) : IIpsTransport
{
    public Task<IpsSubmissionResponse> SendAsync(string messageXml, CancellationToken cancellationToken) =>
        PostAsync(messageXml, possibleDuplicate: false, cancellationToken);

    public Task<IpsSubmissionResponse> ResendAsync(string messageXml, CancellationToken cancellationToken) =>
        PostAsync(messageXml, possibleDuplicate: true, cancellationToken);

    private async Task<IpsSubmissionResponse> PostAsync(string messageXml, bool possibleDuplicate, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.MessagePath.TrimStart('/'))
        {
            Content = new StringContent(messageXml, Encoding.UTF8, "application/xml")
        };
        request.Headers.Add(IpsHeaders.Channel, settings.ParticipantBic.ToUpperInvariant());
        request.Headers.Add(IpsHeaders.Version, settings.IpsVersion);
        if (possibleDuplicate)
        {
            request.Headers.Add(IpsHeaders.ResendPossibleDuplicate, "true");
        }

        request.Headers.Connection.Add("keep-alive");

        var (status, body, headers) = await HttpEvidence.SendAsync(clients, OutgoingHttpRegistration.Ips, request, cancellationToken);
        return new IpsSubmissionResponse(status, body, headers.Select(header => new IpsResponseHeader(header.Name, header.Value)).ToArray());
    }
}
