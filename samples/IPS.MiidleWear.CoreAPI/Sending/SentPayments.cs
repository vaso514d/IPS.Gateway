using System.Collections.Concurrent;
using System.Text.Json;
using IPS.MiidleWear.Contracts.Pacs008;

namespace IPS.MiidleWear.CoreAPI.Sending;

// A pacs.008 this core sent, with the group message id the gateway generated for it (from the gateway's answer).
public sealed record SentPacs008(Pacs008InstantPaymentRequestDto Request, string? MessageId, string? Status);

// The pacs.008 transfers sent since start, by client reference, so a recall (camt.056) can quote the original payment as a
// core system does from its own records. In memory, like the ledger.
public sealed class SentPayments
{
    private readonly ConcurrentDictionary<string, SentPacs008> _sent = new(StringComparer.Ordinal);

    public void Remember(Pacs008InstantPaymentRequestDto request, SendResult result) =>
        _sent[request.ClientReference!] = new SentPacs008(request, Text(result.Response, "messageId"), Text(result.Response, "status"));

    public SentPacs008? Find(string clientReference) => _sent.GetValueOrDefault(clientReference.Trim());

    public IReadOnlyList<object> Snapshot() =>
        [.. _sent.Values.Select(sent => new
        {
            sent.Request.ClientReference,
            sent.MessageId,
            sent.Request.EndToEndId,
            sent.Status,
            sent.Request.Amount,
            sent.Request.Currency,
            CreditorBic = sent.Request.Creditor?.ParticipantBic
        })];

    private static string? Text(JsonElement? response, string name) =>
        response is { ValueKind: JsonValueKind.Object } body && body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
