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

public sealed class IncomingCbsClient(IHttpClientFactory clients, IncomingTransportSettings settings)
    : IIncomingCoreClient, IIncomingReversalClient
{
    public Task<CoreResponse> SubmitAsync(string participantBic, Pacs008Request payment, CancellationToken cancellationToken) =>
        SendAsync(participantBic, HttpMethod.Post, settings.SubmissionPath,
            JsonContent.Create(IncomingPacs008CoreMapping.ToContract(payment)), payment.EndToEndId, cancellationToken);

    public Task<CoreResponse> QueryAsync(string participantBic, string endToEndId, CancellationToken cancellationToken) =>
        SendAsync(participantBic, HttpMethod.Get, settings.StatusPath + "?messageKind=Pacs008&reference=" + Uri.EscapeDataString(endToEndId),
            null, null, cancellationToken);

    public Task<CoreResponse> RequestAsync(ReversalNotification notification, CancellationToken cancellationToken)
    {
        var dto = IncomingReversalContract.Map(notification);
        return SendAsync(notification.ParticipantBic, HttpMethod.Post, settings.ReversalPath, JsonContent.Create(dto),
            $"in:{dto.CoreReference}:{dto.Status}", cancellationToken);
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
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        var (status, body, headers) = await HttpEvidence.SendAsync(clients, IncomingHttpRegistration.Cbs, request, cancellationToken);
        return new(status, body, headers.Select(h => new CoreHeader(h.Name, h.Value)).ToArray());
    }
}
