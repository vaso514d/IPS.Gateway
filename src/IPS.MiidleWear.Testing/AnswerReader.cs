using System.Text.Json;
using System.Text.Json.Serialization;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;

namespace IPS.MiidleWear.Testing;

// The identifiers an answer may echo, and how the gateway compares them. A pacs.008 requires each to be absent or equal
// (an expected null therefore requires it absent); the other kinds check only the identifiers the delivery carries.
internal sealed record ExpectedEcho(string? EndToEndId, string? Id, string? TxId, bool AbsentWhenUnknown);

internal sealed record AnswerVerdict(bool Final, string? Status, string Reason);

// The gateway's reading of a core answer (IncomingCoreReplyInterpreter): HTTP 2xx, JSON with case-insensitive top-level
// names and no duplicate member, Status ACCP or RJCT, and echoed identifiers that match. Anything else is not final.
internal static class AnswerReader
{
    private static readonly JsonSerializerOptions Strict = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowDuplicateProperties = false
    };

    internal static AnswerVerdict ReadExchange(Exchange exchange, ExpectedEcho expected) =>
        exchange.Status == 0 ? new(false, null, exchange.Body) : Read(exchange.Status, exchange.Body, expected);

    internal static AnswerVerdict Read(int status, string body, ExpectedEcho expected)
    {
        if (status is < 200 or >= 300)
        {
            return new(false, null, $"HTTP {status} is not an answer.");
        }

        Answer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<Answer>(body, Strict);
        }
        catch (JsonException error)
        {
            return new(false, null, $"The body is not a readable answer: {error.Message}");
        }

        var outcome = answer?.Status?.ToUpperInvariant();
        if (outcome is not (Pacs008PaymentStatuses.Accepted or Pacs008PaymentStatuses.Rejected))
        {
            return new(false, outcome, $"Status '{answer?.Status}' is not final (ACCP or RJCT).");
        }

        var mismatch = Mismatch("EndToEndId", answer!.EndToEndId, expected.EndToEndId, expected.AbsentWhenUnknown)
            ?? Mismatch("Id", answer.Id, expected.Id, expected.AbsentWhenUnknown)
            ?? Mismatch("TxId", answer.TxId, expected.TxId, expected.AbsentWhenUnknown);
        return mismatch is null ? new(true, outcome, $"{outcome}.") : new(false, outcome, mismatch);
    }

    // An incoming instant payment's answer: status ACCEPTED or REJECTED, and transactionId absent or the delivered one.
    internal static AnswerVerdict ReadIncomingPayment(Exchange exchange, string transactionId) =>
        ReadEnvelopeAnswer(exchange, transactionId, [IncomingPaymentStatuses.Accepted, IncomingPaymentStatuses.Rejected], "final (ACCEPTED or REJECTED)");

    // An incoming FI transfer's answer: any of its four statuses (COMPLETED and REJECTED are final), and transactionId
    // absent or the delivered one. Final here means the gateway can read it.
    internal static AnswerVerdict ReadFiTransfer(Exchange exchange, string transactionId) =>
        ReadEnvelopeAnswer(exchange, transactionId,
            [IncomingFiTransferStatuses.AcceptedForProcessing, IncomingFiTransferStatuses.Processing, IncomingFiTransferStatuses.Completed, IncomingFiTransferStatuses.Rejected],
            "an answer (ACCEPTED_FOR_PROCESSING, PROCESSING, COMPLETED or REJECTED)");

    private static AnswerVerdict ReadEnvelopeAnswer(Exchange exchange, string transactionId, string[] statuses, string expected)
    {
        if (exchange.Status == 0)
        {
            return new(false, null, exchange.Body);
        }

        if (exchange.Status is < 200 or >= 300)
        {
            return new(false, null, $"HTTP {exchange.Status} is not an answer.");
        }

        IncomingAnswer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<IncomingAnswer>(exchange.Body, Strict);
        }
        catch (JsonException error)
        {
            return new(false, null, $"The body is not a readable answer: {error.Message}");
        }

        var outcome = answer?.Status?.ToUpperInvariant();
        if (outcome is null || !statuses.Contains(outcome))
        {
            return new(false, outcome, $"status '{answer?.Status}' is not {expected}.");
        }

        return answer!.TransactionId is null || answer.TransactionId == transactionId
            ? new(true, outcome, $"{outcome}.")
            : new(false, outcome, $"transactionId '{answer.TransactionId}' must be omitted or equal '{transactionId}'.");
    }

    private static string? Mismatch(string name, string? actual, string? expected, bool absentWhenUnknown)
    {
        var matches = actual is null || actual == expected || (expected is null && !absentWhenUnknown);
        if (matches)
        {
            return null;
        }

        return expected is null
            ? $"{name} '{actual}' must be omitted: the delivery does not carry it."
            : $"{name} '{actual}' must be omitted or equal '{expected}'.";
    }

    // The gateway reads both answers with one model, so an incoming payment's answer is also unusable when one of the
    // other answer's members has the wrong type.
    private sealed record IncomingAnswer
    {
        public string? TransactionId { get; init; }
        public string? Status { get; init; }
        public string? ReasonCode { get; init; }
        public string? ReasonDescription { get; init; }
        public string? CoreReference { get; init; }
        public string? EndToEndId { get; init; }
        public string? Id { get; init; }
        public string? TxId { get; init; }
        public DateTimeOffset? ProcessedAtUtc { get; init; }
        public int? InternalErrorCode { get; init; }
        public string? Description { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement> Unmapped { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // The members the gateway reads, with its types: a value of the wrong type makes the whole answer unusable there too.
    private sealed record Answer
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
