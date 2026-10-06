using IPS.Middleware.Application.Payments.Camt056.Validation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Recalls;
using IPS.Middleware.Application.Transactions;

namespace IPS.Middleware.Application.Payments.Camt056;

// The normalized camt.056 content that is sent.
public sealed record ValidatedCamt056(
    string ClientReference,
    string MessageId,
    string RecallId,
    string ParticipantBic,
    DateTimeOffset? AssignmentCreatedAtUtc,
    string OriginalMessageId,
    string OriginalEndToEndId,
    string OriginalTransactionId,
    string OriginalCurrency,
    decimal OriginalAmount,
    DateOnly OriginalSettlementDate,
    string ReasonCode,
    RecalledTransaction Original)
{
    public static Camt056ValidationResult Validate(Camt056Request request, Pacs008Policy policy, DateOnly today)
    {
        var result = new Camt056Validator(policy, today).Validate(request);
        return result.IsValid
            ? new Camt056ValidationResult(Normalize(request, policy), [])
            : new Camt056ValidationResult(null, IntakeErrors.From(result));
    }

    private static ValidatedCamt056 Normalize(Camt056Request request, Pacs008Policy policy) => new(
        ClientReference: request.ClientReference!.Trim(),
        MessageId: request.Id!.Trim(),
        RecallId: request.RecallId!.Trim(),
        ParticipantBic: policy.ParticipantBic,
        AssignmentCreatedAtUtc: request.CreatedAt?.ToUniversalTime(),
        OriginalMessageId: request.OriginalMessageId!.Trim(),
        OriginalEndToEndId: request.OriginalEndToEndId!.Trim(),
        OriginalTransactionId: request.OriginalTransactionId!.Trim(),
        OriginalCurrency: request.OriginalCurrency!.Trim().ToUpperInvariant(),
        OriginalAmount: request.OriginalAmount!.Value,
        OriginalSettlementDate: request.OriginalSettlementDate!.Value,
        ReasonCode: request.ReasonCode!.Trim(),
        Original: RecalledTransaction.From(request.OriginalTransaction!));
}

public sealed record Camt056ValidationResult(ValidatedCamt056? Payment, IReadOnlyList<IntakeValidationError> Errors);
