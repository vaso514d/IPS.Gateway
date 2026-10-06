using System.Text.Json;
using System.Text.Json.Serialization;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Infrastructure.Inbound.Pacs008;

public sealed class IncomingCoreReplyInterpreter : IIncomingCoreReplyInterpreter, IIncomingTransferReplyInterpreter
{
    // Top-level names match case-insensitively; duplicates there or exact duplicates in nested JSON make the reply unusable.
    private static readonly JsonSerializerOptions Strict = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowDuplicateProperties = false
    };

    public CorePaymentResult Interpret(CoreCallCompletion completion, IncomingPacs008Reference original) =>
        Interpret(completion, original.EndToEndId, original.GroupMessageId, original.TransactionId);

    CorePaymentResult IIncomingTransferReplyInterpreter.Interpret(CoreCallCompletion completion, IncomingPacs009 transfer) =>
        Interpret(completion, transfer.EndToEndId, transfer.MessageId, transfer.TransactionId);

    private static CorePaymentResult Interpret(CoreCallCompletion completion, string endToEndId, string? messageId, string? transactionId)
    {
        var unknown = new CorePaymentResult(CoreOutcome.Unknown, completion.ObservedAtUtc, Description: completion.Failure);
        if (completion.Response is not { StatusCode: >= 200 and < 300 } response || Read(response.Body) is not { } reply)
        {
            return unknown;
        }

        var outcome = ReadOutcome(reply.Status);
        if (outcome == CoreOutcome.Unknown || !AnswersOriginal(reply, endToEndId, messageId, transactionId))
        {
            return unknown;
        }

        return new CorePaymentResult(
            outcome,
            reply.ProcessedAtUtc?.ToUniversalTime() ?? completion.ObservedAtUtc,
            reply.CoreReference,
            reply.ReasonCode,
            reply.InternalErrorCode,
            reply.Description);
    }

    private static CoreOutcome ReadOutcome(string? status) => status?.ToUpperInvariant() switch
    {
        "ACCP" => CoreOutcome.Accepted,
        "RJCT" => CoreOutcome.Rejected,
        _ => CoreOutcome.Unknown
    };

    private static bool AnswersOriginal(CoreReply reply, string endToEndId, string? messageId, string? transactionId) =>
        Matches(reply.EndToEndId, endToEndId)
        && Matches(reply.Id, messageId)
        && Matches(reply.TxId, transactionId);

    private static CoreReply? Read(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<CoreReply>(body, Strict);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A supplied identifier must match exactly; a missing one trusts the request or query it answers.
    private static bool Matches(string? actual, string? expected) => actual is null || actual == expected;

    private sealed record CoreReply
    {
        public string? Status { get; init; }
        public string? EndToEndId { get; init; }
        public string? Id { get; init; }
        public string? TxId { get; init; }
        public DateTimeOffset? ProcessedAtUtc { get; init; }
        public string? CoreReference { get; init; }
        public string? ReasonCode { get; init; }
        public int? InternalErrorCode { get; init; }
        public string? Description { get; init; }

        // Kept only so that case-variant duplicates of unmapped names are rejected too.
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Unmapped { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
