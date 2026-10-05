using System.Net.Http.Json;
using System.Text;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Infrastructure.Payments.StatusDelivery;
using IPS.Middleware.Infrastructure.Transport;

namespace IPS.Middleware.Infrastructure.Payments.Transport;

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
