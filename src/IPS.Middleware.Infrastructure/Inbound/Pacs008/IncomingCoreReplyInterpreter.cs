using System.Text.Json;
using System.Text.Json.Serialization;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Domain.Inbound;
using IPS.MiidleWear.Contracts.Pacs008;

namespace IPS.Middleware.Infrastructure.Inbound.Pacs008;

public sealed class IncomingCoreReplyInterpreter : IIncomingCoreReplyInterpreter, IIncomingTransferReplyInterpreter
{
    // Top-level names match case-insensitively; duplicates there or exact duplicates in nested JSON make the reply unusable.
    private static readonly JsonSerializerOptions Strict = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowDuplicateProperties = false
    };

    // An incoming pacs.008 is answered in its own envelope (specification 022): ACCEPTED or REJECTED, echoing at most the
    // payment's transactionId.
    public CorePaymentResult Interpret(CoreCallCompletion completion, IncomingPayment payment) =>
        Interpret(completion, IncomingPaymentOutcome, reply => Matches(reply.TransactionId, IncomingPaymentContract.TransactionId(payment.Id)));

    CorePaymentResult IIncomingTransferReplyInterpreter.Interpret(CoreCallCompletion completion, IIncomingTransferContent transfer)
    {
        var echo = transfer.Echo;
        return Interpret(completion, TransferOutcome, reply =>
            Echoes(reply.EndToEndId, echo.EndToEndId)
            && Echoes(reply.Id, echo.Id)
            && Echoes(reply.TxId, echo.TransactionId));
    }

    private static CorePaymentResult Interpret(CoreCallCompletion completion, Func<string?, CoreOutcome> readOutcome, Func<CoreReply, bool> answersOriginal)
    {
        var unknown = new CorePaymentResult(CoreOutcome.Unknown, completion.ObservedAtUtc, Description: completion.Failure);
        if (completion.Response is not { StatusCode: >= 200 and < 300 } response || Read(response.Body) is not { } reply)
        {
            return unknown;
        }

        var outcome = readOutcome(reply.Status?.ToUpperInvariant());
        if (outcome == CoreOutcome.Unknown || !answersOriginal(reply))
        {
            return unknown;
        }

        return new CorePaymentResult(
            outcome,
            reply.ProcessedAtUtc?.ToUniversalTime() ?? completion.ObservedAtUtc,
            reply.CoreReference,
            reply.ReasonCode,
            reply.InternalErrorCode,
            reply.ReasonDescription ?? reply.Description);
    }

    private static CoreOutcome IncomingPaymentOutcome(string? status) => status switch
    {
        IncomingPaymentStatuses.Accepted => CoreOutcome.Accepted,
        IncomingPaymentStatuses.Rejected => CoreOutcome.Rejected,
        _ => CoreOutcome.Unknown
    };

    private static CoreOutcome TransferOutcome(string? status) => status switch
    {
        Pacs008PaymentStatuses.Accepted => CoreOutcome.Accepted,
        Pacs008PaymentStatuses.Rejected => CoreOutcome.Rejected,
        _ => CoreOutcome.Unknown
    };

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

    // A transfer checks only the identifiers it carries.
    private static bool Echoes(string? actual, string? expected) => actual is null || expected is null || actual == expected;

    // The members of both answers: the incoming payment's (transactionId, reasonDescription) and the transfers' (ids,
    // description); the rest are shared.
    private sealed record CoreReply
    {
        public string? Status { get; init; }
        public string? TransactionId { get; init; }
        public string? EndToEndId { get; init; }
        public string? Id { get; init; }
        public string? TxId { get; init; }
        public DateTimeOffset? ProcessedAtUtc { get; init; }
        public string? CoreReference { get; init; }
        public string? ReasonCode { get; init; }
        public string? ReasonDescription { get; init; }
        public int? InternalErrorCode { get; init; }
        public string? Description { get; init; }

        // Kept only so that case-variant duplicates of unmapped names are rejected too.
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Unmapped { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
