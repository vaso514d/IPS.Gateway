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
        PaymentInitiation = paymentInitiation is null
            ? null
            : paymentInitiation with { Geolocation = Snapshot(paymentInitiation.Geolocation) };
        InitiationChannel = initiationChannel is null
            ? null
            : initiationChannel with { InstrumentCodes = Snapshot(initiationChannel.InstrumentCodes) };
        Remittance = remittance is null
            ? null
            : remittance with { Structured = Snapshot(remittance.Structured) };
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
        if (!result.IsValid)
        {
            return new Pacs008ValidationResult(null, IntakeErrors.From(result));
        }

        return new Pacs008ValidationResult(Normalize(request, policy), []);
    }

    private static ValidatedPacs008 Normalize(Pacs008Request request, Pacs008Policy policy)
    {
        var debtor = request.Debtor!;
        var creditor = request.Creditor!;
        var creditorAccountKind = policy.IsTreasury(creditor.ParticipantBic) ? PaymentAccountKind.Treasury : PaymentAccountKind.Iban;

        return new ValidatedPacs008(
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
            debtorAccount: new PaymentAccount(debtor.Account!.Trim(), PaymentAccountKind.Iban),
            creditorAccount: new PaymentAccount(creditor.Account!.Trim(), creditorAccountKind),
            debtorAgent: new PaymentAgent(policy.ParticipantBic, Optional(debtor.IndirectParticipantBic)),
            creditorAgent: new PaymentAgent(creditor.ParticipantBic!.Trim(), Optional(creditor.IndirectParticipantBic)),
            ultimateDebtor: request.UltimateDebtor is { } ultimateDebtor ? NormalizeParty(ultimateDebtor) : null,
            ultimateCreditor: request.UltimateCreditor is { } ultimateCreditor ? NormalizeParty(ultimateCreditor) : null,
            paymentInitiation: NormalizeInitiation(request.PaymentInitiation),
            initiationChannel: NormalizeChannel(request.InitiationChannelInstrument),
            remittance: NormalizeRemittance(request.Remittance));
    }


    private static PaymentParty NormalizeParty(Pacs008PartyInput party, string? bill = null, Pacs008PostalAddressInput? address = null) =>
        new(
            Kind: (PaymentPartyKind)party.Type!.Value,
            Name: party.Name!.Trim(),
            Identifier: Optional(party.Identifier),
            BillIdentifier: Optional(bill),
            Address: NormalizeAddress(address));

    private static PaymentAddress? NormalizeAddress(Pacs008PostalAddressInput? address)
    {
        if (address is null)
        {
            return null;
        }

        return new PaymentAddress(
            StreetName: Optional(address.StreetName),
            BuildingNumber: Optional(address.BuildingNumber),
            PostCode: Optional(address.PostCode),
            TownName: Optional(address.TownName),
            CountrySubdivision: Optional(address.CountrySubdivision),
            Country: Optional(address.Country),
            AddressLines: address.AddressLines);
    }

    private static PaymentInitiation? NormalizeInitiation(Pacs008PaymentInitiationInput? initiation)
    {
        if (initiation is null)
        {
            return null;
        }

        var geolocation = (initiation.Geolocation ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim());
        return new PaymentInitiation(Optional(initiation.ChannelCode), Snapshot(geolocation));
    }

    private static PaymentInitiationChannel? NormalizeChannel(Pacs008InitiationChannelInstrumentInput? channel)
    {
        if (channel is null)
        {
            return null;
        }

        var instruments = Snapshot(channel.InstrumentCodes!.Select(value => value.Trim()));
        return new PaymentInitiationChannel(channel.ChannelCode!.Trim(), instruments, Optional(channel.ElectronicAddress));
    }

    private static PaymentRemittance? NormalizeRemittance(Pacs008RemittanceInput? remittance)
    {
        if (remittance is null)
        {
            return null;
        }

        var references = Snapshot((remittance.Structured ?? []).Select(NormalizeReference));
        return new PaymentRemittance(Optional(remittance.Unstructured), references);
    }

    private static PaymentRemittanceReference NormalizeReference(Pacs008StructuredRemittanceInput reference) =>
        new(
            Type: reference.ReferenceType!.Trim().ToUpperInvariant(),
            Reference: reference.Reference!.Trim(),
            Issuer: Optional(reference.ReferenceIssuer),
            AdditionalInformation: Optional(reference.AdditionalInformation));

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<T> Snapshot<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
}

public sealed record Pacs008ValidationResult(ValidatedPacs008? Payment, IReadOnlyList<IntakeValidationError> Errors);
