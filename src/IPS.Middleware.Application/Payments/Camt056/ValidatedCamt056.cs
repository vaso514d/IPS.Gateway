using IPS.Middleware.Application.Payments.Camt056.Validation;
using IPS.Middleware.Application.Payments.Pacs008;
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

    private static ValidatedCamt056 Normalize(Camt056Request request, Pacs008Policy policy)
    {
        var original = request.OriginalTransaction!;
        return new ValidatedCamt056(
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
            Original: new RecalledTransaction(
                SettlementDate: original.SettlementDate!.Value,
                Remittance: Remittance(original.Remittance),
                UltimateDebtor: Ultimate(original.UltimateDebtor),
                Debtor: Party(original.Debtor!),
                DebtorAgent: Agent(original.DebtorAgent!),
                CreditorAgent: Agent(original.CreditorAgent!),
                Creditor: Party(original.Creditor!),
                UltimateCreditor: Ultimate(original.UltimateCreditor)));
    }

    private static RecallParty Party(RecallPartyInput party)
    {
        var identifier = Optional(party.Identifier);
        return new RecallParty(party.Name!.Trim(), Address(party.Address), Kind(party.Type, identifier), identifier, party.Account!.Trim());
    }

    private static RecallUltimateParty? Ultimate(RecallUltimatePartyInput? party)
    {
        if (party is null)
        {
            return null;
        }

        var identifier = Optional(party.Identifier);
        return new RecallUltimateParty(party.Name!.Trim(), Kind(party.Type, identifier), identifier);
    }

    // The kind chooses OrgId or PrvtId, so it is kept only together with an identifier.
    private static PaymentPartyKind? Kind(int? type, string? identifier) =>
        identifier is null ? null : (PaymentPartyKind)type!.Value;

    private static RecallAddress? Address(RecallAddressInput? address)
    {
        if (address is null)
        {
            return null;
        }

        var lines = (address.AddressLines ?? []).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToArray();
        string?[] parts =
        [
            Optional(address.StreetName), Optional(address.BuildingNumber), Optional(address.PostCode),
            Optional(address.TownName), Optional(address.CountrySubdivision), Optional(address.Country)
        ];
        // An address with nothing in it is not sent.
        return parts.All(part => part is null) && lines.Length == 0
            ? null
            : new RecallAddress(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], lines);
    }

    private static RecallAgent Agent(RecallAgentInput agent) => new(agent.Bic!.Trim(), Optional(agent.Name));

    private static RecallRemittance? Remittance(RecallRemittanceInput? remittance)
    {
        var unstructured = Optional(remittance?.Unstructured);
        var reference = remittance?.CreditorReference is { } creditorReference
            ? new RecallCreditorReference(Optional(creditorReference.Issuer), creditorReference.Reference!.Trim())
            : null;
        return unstructured is null && reference is null ? null : new RecallRemittance(unstructured, reference);
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

// The recalled payment as quoted by the caller.
public sealed record RecalledTransaction(
    DateOnly SettlementDate,
    RecallRemittance? Remittance,
    RecallUltimateParty? UltimateDebtor,
    RecallParty Debtor,
    RecallAgent DebtorAgent,
    RecallAgent CreditorAgent,
    RecallParty Creditor,
    RecallUltimateParty? UltimateCreditor);

public sealed record RecallParty(string Name, RecallAddress? Address, PaymentPartyKind? Kind, string? Identifier, string Account);

public sealed record RecallUltimateParty(string Name, PaymentPartyKind? Kind, string? Identifier);

public sealed record RecallAddress(
    string? StreetName,
    string? BuildingNumber,
    string? PostCode,
    string? TownName,
    string? CountrySubdivision,
    string? Country,
    IReadOnlyList<string> AddressLines);

public sealed record RecallAgent(string Bic, string? Name);

public sealed record RecallRemittance(string? Unstructured, RecallCreditorReference? CreditorReference);

public sealed record RecallCreditorReference(string? Issuer, string Reference);

public sealed record Camt056ValidationResult(ValidatedCamt056? Payment, IReadOnlyList<IntakeValidationError> Errors);
