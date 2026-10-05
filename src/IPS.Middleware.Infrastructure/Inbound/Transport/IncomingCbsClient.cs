using System.Net.Http.Json;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Transport;

namespace IPS.Middleware.Infrastructure.Inbound.Transport;

public sealed class IncomingCbsClient(IHttpClientFactory clients, IncomingTransportSettings settings)
    : IIncomingCoreClient, IIncomingReversalClient
{
    public Task<CoreResponse> SubmitAsync(string participantBic, Pacs008Request payment, CancellationToken cancellationToken)
    {
        var content = JsonContent.Create(IncomingPacs008CoreMapping.ToContract(payment));
        return SendAsync(participantBic, HttpMethod.Post, settings.SubmissionPath, content, payment.EndToEndId, cancellationToken);
    }

    public Task<CoreResponse> QueryAsync(string participantBic, string endToEndId, CancellationToken cancellationToken)
    {
        var path = settings.StatusPath + "?messageKind=Pacs008&reference=" + Uri.EscapeDataString(endToEndId);
        return SendAsync(participantBic, HttpMethod.Get, path, null, null, cancellationToken);
    }

    public Task<CoreResponse> RequestAsync(ReversalNotification notification, CancellationToken cancellationToken)
    {
        var reversal = IncomingReversalContract.Map(notification);
        var idempotencyKey = $"in:{reversal.CoreReference}:{reversal.Status}";
        return SendAsync(notification.ParticipantBic, HttpMethod.Post, settings.ReversalPath, JsonContent.Create(reversal), idempotencyKey, cancellationToken);
    }

    private async Task<CoreResponse> SendAsync(
        string participant,
        HttpMethod method,
        string path,
        HttpContent? content,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        IncomingHttpRegistration.RequireParticipant(settings, participant);
        using var request = new HttpRequestMessage(method, path.TrimStart('/')) { Content = content };
        if (idempotencyKey is not null)
        {
            request.Headers.Add(IpsHeaders.IdempotencyKey, idempotencyKey);
        }

        var (status, body, headers) = await HttpEvidence.SendAsync(clients, IncomingHttpRegistration.Cbs, request, cancellationToken);
        return new CoreResponse(status, body, headers.Select(header => new CoreHeader(header.Name, header.Value)).ToArray());
    }
}
