using System.Text.Json;
using IPS.Middleware.Application.Abstractions.Inbound;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Infrastructure.Inbound.Pacs008;

public sealed class IncomingCoreReplyInterpreter : IIncomingCoreReplyInterpreter
{
    public CorePaymentResult Interpret(CoreCallCompletion completion, IncomingPacs008Reference original)
    {
        var unknown = new CorePaymentResult(CoreOutcome.Unknown, completion.ObservedAtUtc, Description: completion.Failure);
        if (completion.Response is not { StatusCode: >= 200 and < 300 } response) return unknown;
        try
        {
            using var json = JsonDocument.Parse(response.Body);
            if (json.RootElement.ValueKind != JsonValueKind.Object) return unknown;
            var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in json.RootElement.EnumerateObject())
                if (!fields.TryAdd(field.Name, field.Value)) return unknown;
            string? Text(string name) => fields.TryGetValue(name, out var value) ? value.GetString() : null;
            bool Matches(string name, string? expected) => Text(name) is not { } actual || actual == expected;
            var status = Text("Status");
            var outcome = string.Equals(status, "ACCP", StringComparison.OrdinalIgnoreCase) ? CoreOutcome.Accepted
                : string.Equals(status, "RJCT", StringComparison.OrdinalIgnoreCase) ? CoreOutcome.Rejected : CoreOutcome.Unknown;
            if (outcome == CoreOutcome.Unknown || !Matches("EndToEndId", original.EndToEndId) ||
                !Matches("Id", original.GroupMessageId) || !Matches("TxId", original.TransactionId)) return unknown;
            var at = fields.TryGetValue("ProcessedAtUtc", out var date) && date.ValueKind != JsonValueKind.Null
                ? date.GetDateTimeOffset().ToUniversalTime() : completion.ObservedAtUtc;
            int? code = fields.TryGetValue("InternalErrorCode", out var number) && number.ValueKind != JsonValueKind.Null ? number.GetInt32() : null;
            return new(outcome, at, Text("CoreReference"), Text("ReasonCode"), code, Text("Description"));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            return unknown;
        }
    }
}
