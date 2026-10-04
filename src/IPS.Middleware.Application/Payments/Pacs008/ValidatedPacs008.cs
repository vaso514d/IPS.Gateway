using System.Text.RegularExpressions;
using IPS.Middleware.Application.Payments.Pacs008.Validation;
using IPS.Middleware.Application.Transactions;

namespace IPS.Middleware.Application.Payments.Pacs008;

public sealed class ValidatedPacs008
{
    private ValidatedPacs008(Pacs008Request request, Pacs008Policy policy)
    {
        ClientReference = request.ClientReference!.Trim();
        InstructionId = request.InstructionId!.Trim();
        EndToEndId = request.EndToEndId!.Trim();
        CreationDateTime = request.CreationDateTime!.Value.ToUniversalTime();
        AcceptanceDateTime = request.AcceptanceDateTime!.Value.ToUniversalTime();
        Amount = request.Amount!.Value;
        Currency = request.Currency!.Trim();
        Priority = request.InstructionPriority == "HIGH" ? PaymentPriority.High : PaymentPriority.Normal;
        CategoryPurposeCode = Optional(request.CategoryPurposeCode)?.ToUpperInvariant();
        ParticipantBic = policy.ParticipantBic;
        var debtor = request.Debtor!;
        var creditor = request.Creditor!;
        Debtor = NormalizeParty(debtor, debtor.BillIdentifier, debtor.Address);
        Creditor = NormalizeParty(creditor, null, creditor.Address);
        DebtorAccount = new(debtor.Account!.Trim(), PaymentAccountKind.Iban);
        CreditorAccount = new(creditor.Account!.Trim(), policy.IsTreasury(creditor.ParticipantBic) ? PaymentAccountKind.Treasury : PaymentAccountKind.Iban);
        DebtorAgent = new(policy.ParticipantBic, Optional(debtor.IndirectParticipantBic));
        CreditorAgent = new(creditor.ParticipantBic!.Trim(), Optional(creditor.IndirectParticipantBic));
        UltimateDebtor = request.UltimateDebtor is { } ultimateDebtor ? NormalizeParty(ultimateDebtor) : null;
        UltimateCreditor = request.UltimateCreditor is { } ultimateCreditor ? NormalizeParty(ultimateCreditor) : null;
        PaymentInitiation = NormalizeInitiation(request.PaymentInitiation);
        InitiationChannel = NormalizeChannel(request.InitiationChannelInstrument);
        Remittance = NormalizeRemittance(request.Remittance);
    }

    public string ClientReference { get; }
    public string InstructionId { get; }
    public string EndToEndId { get; }
    public DateTimeOffset CreationDateTime { get; }
    public DateTimeOffset AcceptanceDateTime { get; }
    public decimal Amount { get; }
    public string Currency { get; }
    public PaymentPriority Priority { get; }
    public string? CategoryPurposeCode { get; }
    public string ParticipantBic { get; }
    public PaymentParty Debtor { get; }
    public PaymentParty Creditor { get; }
    public PaymentAccount DebtorAccount { get; }
    public PaymentAccount CreditorAccount { get; }
    public PaymentAgent DebtorAgent { get; }
    public PaymentAgent CreditorAgent { get; }
    public PaymentParty? UltimateDebtor { get; }
    public PaymentParty? UltimateCreditor { get; }
    public PaymentInitiation? PaymentInitiation { get; }
    public PaymentInitiationChannel? InitiationChannel { get; }
    public PaymentRemittance? Remittance { get; }

    public static Pacs008ValidationResult Validate(Pacs008Request request, Pacs008Policy policy)
    {
        var result = new Pacs008Validator(policy).Validate(request);
        var errors = result.Errors.Select(error => new IntakeValidationError(ErrorPath(error.PropertyName), error.ErrorMessage)).ToArray();
        return new(result.IsValid ? new(request, policy) : null, Array.AsReadOnly(errors));
    }

    // Retain the existing camel-case, unindexed error paths at this boundary.
    private static string ErrorPath(string property) => string.Join('.',
        Regex.Replace(property, @"\[\d+\]", "").Split('.').Select(part => char.ToLowerInvariant(part[0]) + part[1..]));

    private static PaymentParty NormalizeParty(Pacs008PartyInput party, string? bill = null, Pacs008PostalAddressInput? address = null) =>
        new(Kind: (PaymentPartyKind)party.Type!.Value, Name: party.Name!.Trim(),
            Identifier: Optional(party.Identifier), BillIdentifier: Optional(bill), Address: NormalizeAddress(address));

    private static PaymentAddress? NormalizeAddress(Pacs008PostalAddressInput? address) => address is null ? null :
        new(StreetName: Optional(address.StreetName), BuildingNumber: Optional(address.BuildingNumber),
            PostCode: Optional(address.PostCode), TownName: Optional(address.TownName),
            CountrySubdivision: Optional(address.CountrySubdivision), Country: Optional(address.Country),
            AddressLines: address.AddressLines);

    private static PaymentInitiation? NormalizeInitiation(Pacs008PaymentInitiationInput? initiation)
    {
        if (initiation is null) return null;
        var geolocation = (initiation.Geolocation ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim());
        return new(ChannelCode: Optional(initiation.ChannelCode), Geolocation: Snapshot(geolocation));
    }

    private static PaymentInitiationChannel? NormalizeChannel(Pacs008InitiationChannelInstrumentInput? channel)
    {
        if (channel is null) return null;
        var instruments = Snapshot(channel.InstrumentCodes!.Select(value => value.Trim()));
        return new(ChannelCode: channel.ChannelCode!.Trim(), InstrumentCodes: instruments,
            ElectronicAddress: Optional(channel.ElectronicAddress));
    }

    private static PaymentRemittance? NormalizeRemittance(Pacs008RemittanceInput? remittance)
    {
        if (remittance is null) return null;
        var references = Snapshot((remittance.Structured ?? []).Select(NormalizeReference));
        return new(Unstructured: Optional(remittance.Unstructured), Structured: references);
    }

    private static PaymentRemittanceReference NormalizeReference(Pacs008StructuredRemittanceInput reference) =>
        new(Type: reference.ReferenceType!.Trim().ToUpperInvariant(), Reference: reference.Reference!.Trim(),
            Issuer: Optional(reference.ReferenceIssuer), AdditionalInformation: Optional(reference.AdditionalInformation));

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IReadOnlyList<T> Snapshot<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
}

public sealed record Pacs008ValidationResult(ValidatedPacs008? Payment, IReadOnlyList<IntakeValidationError> Errors);
