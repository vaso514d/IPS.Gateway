using System.Net.Http.Json;
using System.Text;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Infrastructure.Payments.StatusDelivery;
using IPS.Middleware.Infrastructure.Transport;

namespace IPS.Middleware.Infrastructure.Payments.Transport;

public sealed class OutgoingIpsClient(IHttpClientFactory clients, OutgoingTransportSettings settings) : IIpsTransport
{
    public async Task<IpsSubmissionResponse> SendAsync(string messageXml, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.MessagePath.TrimStart('/'))
        { Content = new StringContent(messageXml, Encoding.UTF8, "application/xml") };
        request.Headers.Add("X-MONTRAN-IPS-Channel", settings.ParticipantBic.ToUpperInvariant());
        request.Headers.Add("X-MONTRAN-IPS-Version", settings.IpsVersion);
        request.Headers.Connection.Add("keep-alive");
        var (status, body, headers) = await HttpEvidence.SendAsync(clients, OutgoingHttpRegistration.Ips, request, cancellationToken);
        return new(status, body, headers.Select(h => new IpsResponseHeader(h.Name, h.Value)).ToArray());
    }
}

public sealed class OutgoingStatusClient(IHttpClientFactory clients, OutgoingTransportSettings settings) : IOutgoingStatusReceiver
{
    public async Task<int> SendAsync(OutgoingStatus status, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.CallbackPath.TrimStart('/'))
        { Content = JsonContent.Create(OutgoingStatusContract.Map(status)) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        var evidence = await HttpEvidence.SendAsync(clients, OutgoingHttpRegistration.Cbs, request, cancellationToken);
        return evidence.Status;
    }
}
