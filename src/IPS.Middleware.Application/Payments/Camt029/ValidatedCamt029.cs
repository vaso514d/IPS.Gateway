using IPS.Middleware.Application.Payments.Camt029.Validation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Recalls;
using IPS.Middleware.Application.Transactions;

namespace IPS.Middleware.Application.Payments.Camt029;

// The normalized camt.029 content that is sent. The answer is always a refusal (RJCR), so it is not a field.
public sealed record ValidatedCamt029(
    string ClientReference,
    string MessageId,
    string CancellationStatusId,
    string ParticipantBic,
    DateTimeOffset? AssignmentCreatedAtUtc,
    string OriginalMessageId,
    string OriginalEndToEndId,
    string OriginalTransactionId,
    string ReasonCode,
    string? AdditionalInformation,
    string OriginalCurrency,
    decimal OriginalAmount,
    RecalledTransaction Original)
{
    public static Camt029ValidationResult Validate(Camt029Request request, Pacs008Policy policy, DateOnly today)
    {
        var result = new Camt029Validator(policy, today).Validate(request);
        return result.IsValid
            ? new Camt029ValidationResult(Normalize(request, policy), [])
            : new Camt029ValidationResult(null, IntakeErrors.From(result));
    }

    private static ValidatedCamt029 Normalize(Camt029Request request, Pacs008Policy policy) => new(
        ClientReference: request.ClientReference!.Trim(),
        MessageId: request.Id!.Trim(),
        CancellationStatusId: request.CancellationStatusId!.Trim(),
        ParticipantBic: policy.ParticipantBic,
        AssignmentCreatedAtUtc: request.CreatedAt?.ToUniversalTime(),
        OriginalMessageId: request.OriginalMessageId!.Trim(),
        OriginalEndToEndId: request.OriginalEndToEndId!.Trim(),
        OriginalTransactionId: request.OriginalTransactionId!.Trim(),
        ReasonCode: request.ReasonCode!.Trim(),
        AdditionalInformation: string.IsNullOrWhiteSpace(request.AdditionalInformation) ? null : request.AdditionalInformation.Trim(),
        OriginalCurrency: request.OriginalTransaction!.Currency!.Trim().ToUpperInvariant(),
        OriginalAmount: request.OriginalTransaction.Amount!.Value,
        Original: RecalledTransaction.From(request.OriginalTransaction!));
}

public sealed record Camt029ValidationResult(ValidatedCamt029? Payment, IReadOnlyList<IntakeValidationError> Errors);
