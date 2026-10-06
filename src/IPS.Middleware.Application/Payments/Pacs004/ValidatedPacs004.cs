using IPS.Middleware.Application.Payments.Pacs004.Validation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;

namespace IPS.Middleware.Application.Payments.Pacs004;

// The normalized pacs.004 content that is sent. Fields the source validates for the caller only (originator, additional
// info, the SWIFT codes, the transaction type code, the instructing agent, UETR, original instruction id) are not kept.
public sealed record ValidatedPacs004(
    string ClientReference,
    string ReturnId,
    string ParticipantBic,
    string InstructedAgentBic,
    string? SenderIndirectParticipant,
    DateOnly ValueDate,
    string Currency,
    decimal Amount,
    string ReturnReasonCode,
    ReturnedParty Debtor,
    ReturnedParty Creditor,
    OriginalPaymentReference Original)
{
    // The only return reason IPS allows for instant payments (Annex D 3.2.3.j).
    public const string FullReturnReason = "FOCR";

    public static Pacs004ValidationResult Validate(Pacs004Request request, Pacs008Policy policy)
    {
        var result = new Pacs004Validator(policy).Validate(request);
        return result.IsValid
            ? new Pacs004ValidationResult(Normalize(request, policy), [])
            : new Pacs004ValidationResult(null, IntakeErrors.From(result));
    }

    private static ValidatedPacs004 Normalize(Pacs004Request request, Pacs008Policy policy)
    {
        var original = request.Original!;
        var currency = request.Currency!.Trim().ToUpperInvariant();
        return new ValidatedPacs004(
            ClientReference: request.ClientReference!.Trim(),
            ReturnId: request.Id!.Trim(),
            ParticipantBic: policy.ParticipantBic,
            InstructedAgentBic: request.InstructedAgent!.Trim(),
            SenderIndirectParticipant: Optional(request.SenderIndirectParticipant),
            ValueDate: request.ValueDate!.Value,
            Currency: currency,
            Amount: request.Amount!.Value,
            ReturnReasonCode: Optional(request.ReturnReasonCode) ?? FullReturnReason,
            Debtor: Party(request.Debtor!),
            Creditor: Party(request.Creditor!),
            Original: new OriginalPaymentReference(
                TransactionId: original.TransactionId!.Trim(),
                EndToEndId: original.EndToEndId!.Trim(),
                ValueDate: original.ValueDate!.Value,
                MessageId: Optional(original.OriginalMessageId),
                MessageNameId: Optional(original.OriginalMessageNameId),
                Amount: original.Amount ?? request.Amount.Value,
                Currency: Optional(original.Currency)?.ToUpperInvariant() ?? currency));
    }

    private static ReturnedParty Party(Pacs004PartyInput party)
    {
        var identifier = Optional(party.Identifier);
        return new ReturnedParty(
            party.Name!.Trim(),
            identifier is null ? null : (PaymentPartyKind)party.Type!.Value,
            identifier,
            party.Account!.Trim());
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

// The kind and identifier are both present or both absent.
public sealed record ReturnedParty(string Name, PaymentPartyKind? Kind, string? Identifier, string Account);

// The original payment as the caller quotes it. The amount and currency default to the returned ones (a full return).
public sealed record OriginalPaymentReference(
    string TransactionId,
    string EndToEndId,
    DateOnly ValueDate,
    string? MessageId,
    string? MessageNameId,
    decimal Amount,
    string Currency);

public sealed record Pacs004ValidationResult(ValidatedPacs004? Payment, IReadOnlyList<IntakeValidationError> Errors);
