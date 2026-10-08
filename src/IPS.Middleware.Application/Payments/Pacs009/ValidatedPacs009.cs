using System.Text.RegularExpressions;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pacs009.Validation;
using IPS.Middleware.Application.Transactions;

namespace IPS.Middleware.Application.Payments.Pacs009;

// The normalized pacs.009 content that is sent. Optional wire fields the source never sends are validated but not kept.
public sealed record ValidatedPacs009(
    string ClientReference,
    string MessageId,
    string TransactionId,
    string ParticipantBic,
    string DebtorAgentBic,
    string CreditorAgentBic,
    string? InstructionId,
    string EndToEndId,
    DateOnly ValueDate,
    string Currency,
    decimal Amount,
    string? CategoryPurpose,
    bool CategoryPurposeIsProprietary,
    string? Purpose,
    bool PurposeIsProprietary,
    string? AdditionalPurpose,
    string? DebtorAccount,
    string? CreditorAccount)
{
    public static Pacs009ValidationResult Validate(Pacs009Request request, Pacs008Policy policy)
    {
        var result = new Pacs009Validator(policy).Validate(request);
        return result.IsValid
            ? new Pacs009ValidationResult(Normalize(request, policy), [])
            : new Pacs009ValidationResult(null, IntakeErrors.From(result));
    }

    private static ValidatedPacs009 Normalize(Pacs009Request request, Pacs008Policy policy)
    {
        var messageId = request.Id!.Trim();
        var category = request.CategoryPurpose;
        return new ValidatedPacs009(
            ClientReference: request.ClientReference!.Trim(),
            MessageId: messageId,
            TransactionId: Optional(request.TransactionId) ?? messageId,
            ParticipantBic: policy.ParticipantBic,
            DebtorAgentBic: request.DebtorAgent!.Bic!.Trim(),
            CreditorAgentBic: request.CreditorAgent!.Bic!.Trim(),
            InstructionId: Optional(request.InstructionId),
            EndToEndId: request.EndToEndId!.Trim(),
            ValueDate: request.ValueDate!.Value,
            Currency: request.Currency!.Trim().ToUpperInvariant(),
            Amount: request.Amount!.Value,
            CategoryPurpose: Optional(category?.Value),
            CategoryPurposeIsProprietary: category?.Type == 1,
            Purpose: Optional(request.Purpose),
            // A four-letter uppercase purpose is an ISO purpose code; anything else is proprietary text.
            PurposeIsProprietary: Optional(request.Purpose) is { } purpose && !Regex.IsMatch(purpose, Pacs008Text.Code4Upper),
            AdditionalPurpose: Optional(request.AdditionalPurpose),
            DebtorAccount: Optional(request.DebtorAccount),
            CreditorAccount: Optional(request.CreditorAccount));
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record Pacs009ValidationResult(ValidatedPacs009? Payment, IReadOnlyList<IntakeValidationError> Errors);
