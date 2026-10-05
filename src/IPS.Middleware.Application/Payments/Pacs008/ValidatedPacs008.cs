using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IPS.Middleware.Application.Payments.Pacs008.Validation;
using IPS.Middleware.Application.Transactions;

namespace IPS.Middleware.Application.Payments.Pacs008;

public sealed class ValidatedPacs008
{
    // Only validation creates new values; JSON restores an accepted snapshot without re-applying current policy.
    [JsonConstructor]
    private ValidatedPacs008(
        string clientReference,
        string instructionId,
        string endToEndId,
        DateTimeOffset creationDateTime,
        DateTimeOffset acceptanceDateTime,
        decimal amount,
        string currency,
        PaymentPriority priority,
        string? categoryPurposeCode,
        string participantBic,
        PaymentParty debtor,
        PaymentParty creditor,
        PaymentAccount debtorAccount,
        PaymentAccount creditorAccount,
        PaymentAgent debtorAgent,
        PaymentAgent creditorAgent,
        PaymentParty? ultimateDebtor,
        PaymentParty? ultimateCreditor,
        PaymentInitiation? paymentInitiation,
        PaymentInitiationChannel? initiationChannel,
        PaymentRemittance? remittance)
    {
        ClientReference = clientReference;
        InstructionId = instructionId;
        EndToEndId = endToEndId;
        CreationDateTime = creationDateTime;
        AcceptanceDateTime = acceptanceDateTime;
        Amount = amount;
        Currency = currency;
        Priority = priority;
        CategoryPurposeCode = categoryPurposeCode;
        ParticipantBic = participantBic;
        Debtor = debtor;
        Creditor = creditor;
        DebtorAccount = debtorAccount;
        CreditorAccount = creditorAccount;
        DebtorAgent = debtorAgent;
        CreditorAgent = creditorAgent;
        UltimateDebtor = ultimateDebtor;
        UltimateCreditor = ultimateCreditor;
        PaymentInitiation = paymentInitiation is null ? null : new PaymentInitiation(paymentInitiation)
        {
            Geolocation = Snapshot(paymentInitiation.Geolocation)
        };
        InitiationChannel = initiationChannel is null ? null : new PaymentInitiationChannel(initiationChannel)
        {
            InstrumentCodes = Snapshot(initiationChannel.InstrumentCodes)
        };
        Remittance = remittance is null ? null : new PaymentRemittance(remittance)
        {
            Structured = Snapshot(remittance.Structured)
        };
    }

    private static ValidatedPacs008 Normalize(Pacs008Request request, Pacs008Policy policy)
    {
        var debtor = request.Debtor!;
        var creditor = request.Creditor!;
        return new(
            clientReference: request.ClientReference!.Trim(),
            instructionId: request.InstructionId!.Trim(),
            endToEndId: request.EndToEndId!.Trim(),
            creationDateTime: request.CreationDateTime!.Value.ToUniversalTime(),
            acceptanceDateTime: request.AcceptanceDateTime!.Value.ToUniversalTime(),
            amount: request.Amount!.Value,
            currency: request.Currency!.Trim(),
            priority: request.InstructionPriority == "HIGH" ? PaymentPriority.High : PaymentPriority.Normal,
            categoryPurposeCode: Optional(request.CategoryPurposeCode)?.ToUpperInvariant(),
            participantBic: policy.ParticipantBic,
            debtor: NormalizeParty(debtor, debtor.BillIdentifier, debtor.Address),
            creditor: NormalizeParty(creditor, null, creditor.Address),
            debtorAccount: new(debtor.Account!.Trim(), PaymentAccountKind.Iban),
            creditorAccount: new(creditor.Account!.Trim(), policy.IsTreasury(creditor.ParticipantBic) ? PaymentAccountKind.Treasury : PaymentAccountKind.Iban),
            debtorAgent: new(policy.ParticipantBic, Optional(debtor.IndirectParticipantBic)),
            creditorAgent: new(creditor.ParticipantBic!.Trim(), Optional(creditor.IndirectParticipantBic)),
            ultimateDebtor: request.UltimateDebtor is { } ultimateDebtor ? NormalizeParty(ultimateDebtor) : null,
            ultimateCreditor: request.UltimateCreditor is { } ultimateCreditor ? NormalizeParty(ultimateCreditor) : null,
            paymentInitiation: NormalizeInitiation(request.PaymentInitiation),
            initiationChannel: NormalizeChannel(request.InitiationChannelInstrument),
            remittance: NormalizeRemittance(request.Remittance));
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
        return new(result.IsValid ? Normalize(request, policy) : null, Array.AsReadOnly(errors));
    }

    // Retain the existing camel-case, unindexed error paths at this boundary.
    private static string ErrorPath(string property) => string.Join('.',
        Regex.Replace(property, @"\[\d+\]", "").Split('.').Select(part => char.ToLowerInvariant(part[0]) + part[1..]));
    private static PaymentParty NormalizeParty(
        Pacs008PartyInput party,
        string? bill = null,
        Pacs008PostalAddressInput? address = null) =>
        new(
            kind: (PaymentPartyKind)party.Type!.Value,
            name: party.Name!.Trim(),
            identifier: Optional(party.Identifier),
            billIdentifier: Optional(bill),
            address: NormalizeAddress(address));

    private static PaymentAddress? NormalizeAddress(Pacs008PostalAddressInput? address) =>
        address is null ? null : new(
            streetName: Optional(address.StreetName),
            buildingNumber: Optional(address.BuildingNumber),
            postCode: Optional(address.PostCode),
            townName: Optional(address.TownName),
            countrySubdivision: Optional(address.CountrySubdivision),
            country: Optional(address.Country),
            addressLines: address.AddressLines);

    private static PaymentInitiation? NormalizeInitiation(Pacs008PaymentInitiationInput? initiation)
    {
        if (initiation is null)
        {
            return null;
        }

        var geolocation = (initiation.Geolocation ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim());
        return new(channelCode: Optional(initiation.ChannelCode), geolocation: Snapshot(geolocation));
    }

    private static PaymentInitiationChannel? NormalizeChannel(Pacs008InitiationChannelInstrumentInput? channel)
    {
        if (channel is null)
        {
            return null;
        }

        var instruments = Snapshot(channel.InstrumentCodes!.Select(value => value.Trim()));
        return new(channelCode: channel.ChannelCode!.Trim(), instrumentCodes: instruments,
            electronicAddress: Optional(channel.ElectronicAddress));
    }

    private static PaymentRemittance? NormalizeRemittance(Pacs008RemittanceInput? remittance)
    {
        if (remittance is null)
        {
            return null;
        }

        var references = Snapshot((remittance.Structured ?? []).Select(NormalizeReference));
        return new(unstructured: Optional(remittance.Unstructured), structured: references);
    }

    private static PaymentRemittanceReference NormalizeReference(Pacs008StructuredRemittanceInput reference) =>
        new(
            type: reference.ReferenceType!.Trim().ToUpperInvariant(),
            reference: reference.Reference!.Trim(),
            issuer: Optional(reference.ReferenceIssuer),
            additionalInformation: Optional(reference.AdditionalInformation));

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IReadOnlyList<T> Snapshot<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
}

public sealed class Pacs008ValidationResult
{
    public Pacs008ValidationResult(ValidatedPacs008? payment, IReadOnlyList<IntakeValidationError> errors)
    {
        Payment = payment;
        Errors = errors;
    }

    public ValidatedPacs008? Payment { get; init; }
    public IReadOnlyList<IntakeValidationError> Errors { get; init; }
}
