using System.Net.Http.Json;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Inbound.Transfers;
using IPS.Middleware.Infrastructure.Transport;
using IPS.MiidleWear.Contracts.Transactions;

namespace IPS.Middleware.Infrastructure.Inbound.Transport;

public sealed class IncomingCbsClient(IHttpClientFactory clients, IncomingTransportSettings settings)
    : IIncomingCoreClient, IIncomingReversalClient, IIncomingTransferCoreClient
{
    public Task<CoreResponse> SubmitAsync(string participantBic, Pacs008Request payment, CancellationToken cancellationToken)
    {
        var content = JsonContent.Create(IncomingPacs008CoreMapping.ToContract(payment));
        return SendAsync(participantBic, HttpMethod.Post, settings.SubmissionPath, content, payment.EndToEndId, cancellationToken);
    }

    public Task<CoreResponse> QueryAsync(string participantBic, string endToEndId, CancellationToken cancellationToken) =>
        QueryAsync(participantBic, nameof(IpsMessageKind.Pacs008), endToEndId, cancellationToken);

    public Task<CoreResponse> SubmitAsync(string participantBic, IIncomingTransferContent transfer, CancellationToken cancellationToken)
    {
        var (path, content) = transfer switch
        {
            IncomingPacs009 pacs009 => (settings.Pacs009SubmissionPath, JsonContent.Create(IncomingPacs009CoreMapping.ToContract(pacs009))),
            IncomingPacs004 pacs004 => (settings.Pacs004SubmissionPath, JsonContent.Create(IncomingPacs004CoreMapping.ToContract(pacs004))),
            IncomingPain001 pain001 => (settings.Pain001SubmissionPath, JsonContent.Create(IncomingPain001CoreMapping.ToContract(pain001))),
            IncomingCamt056 camt056 => (settings.Camt056SubmissionPath, JsonContent.Create(IncomingRecallCoreMapping.ToContract(camt056))),
            IncomingCamt055 camt055 => (settings.Camt055SubmissionPath, JsonContent.Create(IncomingCamt055CoreMapping.ToContract(camt055))),
            IncomingCamt029 camt029 => (settings.Camt029SubmissionPath, JsonContent.Create(IncomingRecallCoreMapping.ToContract(camt029))),
            _ => throw new ArgumentOutOfRangeException(nameof(transfer), transfer.GetType().Name, "Unsupported incoming transfer.")
        };
        return SendAsync(participantBic, HttpMethod.Post, path, content, transfer.Key, cancellationToken);
    }

    Task<CoreResponse> IIncomingTransferCoreClient.QueryAsync(string participantBic, string kind, string key, CancellationToken cancellationToken)
    {
        var messageKind = kind switch
        {
            PaymentMessageTypes.Pacs009 => nameof(IpsMessageKind.Pacs009),
            PaymentMessageTypes.Pacs004 => nameof(IpsMessageKind.Pacs004),
            PaymentMessageTypes.Pain001 => nameof(IpsMessageKind.Pain001),
            PaymentMessageTypes.Camt056 => nameof(IpsMessageKind.Camt056),
            PaymentMessageTypes.Camt055 => nameof(IpsMessageKind.Camt055),
            PaymentMessageTypes.Camt029 => nameof(IpsMessageKind.Camt029),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported incoming transfer kind.")
        };
        return QueryAsync(participantBic, messageKind, key, cancellationToken);
    }

    private Task<CoreResponse> QueryAsync(string participantBic, string messageKind, string reference, CancellationToken cancellationToken)
    {
        var path = settings.StatusPath + "?messageKind=" + messageKind + "&reference=" + Uri.EscapeDataString(reference);
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
