using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pain002.Validation;
using IPS.Middleware.Application.Transactions;

namespace IPS.Middleware.Application.Payments.Pain002;

// The normalized pain.002 content that is sent. The report is always a refusal (RJCT), so it is not a field.
public sealed record ValidatedPain002(
    string ClientReference,
    string MessageId,
    string ParticipantBic,
    DateTimeOffset? CreatedAtUtc,
    string OriginalMessageId,
    string OriginalPaymentInformationId,
    string ReasonCode,
    string? AdditionalInformation,
    string? OriginatorName)
{
    public static Pain002ValidationResult Validate(Pain002Request request, Pacs008Policy policy)
    {
        var result = new Pain002Validator().Validate(request);
        return result.IsValid
            ? new Pain002ValidationResult(Normalize(request, policy), [])
            : new Pain002ValidationResult(null, IntakeErrors.From(result));
    }

    private static ValidatedPain002 Normalize(Pain002Request request, Pacs008Policy policy) => new(
        ClientReference: request.ClientReference!.Trim(),
        MessageId: request.Id!.Trim(),
        ParticipantBic: policy.ParticipantBic,
        CreatedAtUtc: request.CreatedAt?.ToUniversalTime(),
        OriginalMessageId: request.OriginalMessageId!.Trim(),
        OriginalPaymentInformationId: request.OriginalPaymentInformationId!.Trim(),
        ReasonCode: request.ReasonCode!.Trim(),
        AdditionalInformation: Optional(request.AdditionalInformation),
        OriginatorName: Optional(request.OriginatorName));

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record Pain002ValidationResult(ValidatedPain002? Payment, IReadOnlyList<IntakeValidationError> Errors);
