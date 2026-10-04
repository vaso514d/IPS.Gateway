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

    private async Task<IpsSubmissionResponse> SendAsync(string client, HttpMethod method, string participant, HttpContent? content,
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

    private async Task<CoreResponse> SendAsync(string participant, HttpMethod method, string path, HttpContent? content,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        IncomingHttpRegistration.RequireParticipant(settings, participant);
        using var request = new HttpRequestMessage(method, path.TrimStart('/')) { Content = content };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        var (status, body, headers) = await HttpEvidence.SendAsync(clients, IncomingHttpRegistration.Cbs, request, cancellationToken);
        return new(status, body, headers.Select(h => new CoreHeader(h.Name, h.Value)).ToArray());
    }
}

/// <summary>One HTTP attempt whose status, complete body and every header value reach the interpreters unchanged.</summary>
internal static class HttpEvidence
{
    internal static async Task<(int Status, string Body, IEnumerable<(string Name, string Value)> Headers)> SendAsync(
        IHttpClientFactory clients, string name, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var client = clients.CreateClient(name);
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var headers = response.Headers.Concat(response.Content.Headers).Concat(response.TrailingHeaders)
            .SelectMany(header => header.Value.Select(value => (header.Key, value))).ToArray();
        return ((int)response.StatusCode, body, headers);
    }
}
