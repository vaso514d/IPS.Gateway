namespace IPS.Middleware.Application.Payments.Pacs008;

public sealed class Pacs008Request : IEquatable<Pacs008Request>
{
    public Pacs008Request()
    {
    }

    public Pacs008Request(Pacs008Request original)
    {
        ClientReference = original.ClientReference;
        InstructionId = original.InstructionId;
        EndToEndId = original.EndToEndId;
        CreationDateTime = original.CreationDateTime;
        AcceptanceDateTime = original.AcceptanceDateTime;
        Amount = original.Amount;
        Currency = original.Currency;
        InstructionPriority = original.InstructionPriority;
        CategoryPurposeCode = original.CategoryPurposeCode;
        Debtor = original.Debtor;
        Creditor = original.Creditor;
        UltimateDebtor = original.UltimateDebtor;
        UltimateCreditor = original.UltimateCreditor;
        PaymentInitiation = original.PaymentInitiation;
        InitiationChannelInstrument = original.InitiationChannelInstrument;
        Remittance = original.Remittance;
    }

    public string? ClientReference { get; init; }
    public string? InstructionId { get; init; }
    public string? EndToEndId { get; init; }
    public DateTimeOffset? CreationDateTime { get; init; }
    public DateTimeOffset? AcceptanceDateTime { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public string? InstructionPriority { get; init; }
    public string? CategoryPurposeCode { get; init; }
    public Pacs008DebtorInput? Debtor { get; init; }
    public Pacs008CreditorInput? Creditor { get; init; }
    public Pacs008UltimatePartyInput? UltimateDebtor { get; init; }
    public Pacs008UltimatePartyInput? UltimateCreditor { get; init; }
    public Pacs008PaymentInitiationInput? PaymentInitiation { get; init; }
    public Pacs008InitiationChannelInstrumentInput? InitiationChannelInstrument { get; init; }
    public Pacs008RemittanceInput? Remittance { get; init; }

    public bool Equals(Pacs008Request? other) => other is not null &&
            Equals(ClientReference, other.ClientReference) &&
            Equals(InstructionId, other.InstructionId) &&
            Equals(EndToEndId, other.EndToEndId) &&
            Equals(CreationDateTime, other.CreationDateTime) &&
            Equals(AcceptanceDateTime, other.AcceptanceDateTime) &&
            Equals(Amount, other.Amount) &&
            Equals(Currency, other.Currency) &&
            Equals(InstructionPriority, other.InstructionPriority) &&
            Equals(CategoryPurposeCode, other.CategoryPurposeCode) &&
            Equals(Debtor, other.Debtor) &&
            Equals(Creditor, other.Creditor) &&
            Equals(UltimateDebtor, other.UltimateDebtor) &&
            Equals(UltimateCreditor, other.UltimateCreditor) &&
            Equals(PaymentInitiation, other.PaymentInitiation) &&
            Equals(InitiationChannelInstrument, other.InitiationChannelInstrument) &&
            Equals(Remittance, other.Remittance);
    public override bool Equals(object? obj) => obj is Pacs008Request other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ClientReference);
        hash.Add(InstructionId);
        hash.Add(EndToEndId);
        hash.Add(CreationDateTime);
        hash.Add(AcceptanceDateTime);
        hash.Add(Amount);
        hash.Add(Currency);
        hash.Add(InstructionPriority);
        hash.Add(CategoryPurposeCode);
        hash.Add(Debtor);
        hash.Add(Creditor);
        hash.Add(UltimateDebtor);
        hash.Add(UltimateCreditor);
        hash.Add(PaymentInitiation);
        hash.Add(InitiationChannelInstrument);
        hash.Add(Remittance);
        return hash.ToHashCode();
    }

    public static bool operator ==(Pacs008Request? left, Pacs008Request? right) => Equals(left, right);
    public static bool operator !=(Pacs008Request? left, Pacs008Request? right) => !Equals(left, right);
}

public sealed class Pacs008DebtorInput : Pacs008PartyInput, IEquatable<Pacs008DebtorInput>
{
    public Pacs008DebtorInput()
    {
    }

    public Pacs008DebtorInput(Pacs008DebtorInput original)
    {
        ParticipantBic = original.ParticipantBic;
        BillIdentifier = original.BillIdentifier;
        Address = original.Address;
        IndirectParticipantBic = original.IndirectParticipantBic;
        Account = original.Account;
        Type = original.Type;
        Name = original.Name;
        Identifier = original.Identifier;
    }

    public string? ParticipantBic { get; init; }
    public string? BillIdentifier { get; init; }
    public Pacs008PostalAddressInput? Address { get; init; }
    public string? IndirectParticipantBic { get; init; }
    public string? Account { get; init; }

    public bool Equals(Pacs008DebtorInput? other) => other is not null &&
            Equals(ParticipantBic, other.ParticipantBic) &&
            Equals(BillIdentifier, other.BillIdentifier) &&
            Equals(Address, other.Address) &&
            Equals(IndirectParticipantBic, other.IndirectParticipantBic) &&
            Equals(Account, other.Account) &&
            Equals(Type, other.Type) &&
            Equals(Name, other.Name) &&
            Equals(Identifier, other.Identifier);
    public override bool Equals(object? obj) => obj is Pacs008DebtorInput other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ParticipantBic);
        hash.Add(BillIdentifier);
        hash.Add(Address);
        hash.Add(IndirectParticipantBic);
        hash.Add(Account);
        hash.Add(Type);
        hash.Add(Name);
        hash.Add(Identifier);
        return hash.ToHashCode();
    }

    public static bool operator ==(Pacs008DebtorInput? left, Pacs008DebtorInput? right) => Equals(left, right);
    public static bool operator !=(Pacs008DebtorInput? left, Pacs008DebtorInput? right) => !Equals(left, right);
}

public sealed class Pacs008CreditorInput : Pacs008PartyInput, IEquatable<Pacs008CreditorInput>
{
    public Pacs008CreditorInput()
    {
    }

    public Pacs008CreditorInput(Pacs008CreditorInput original)
    {
        Address = original.Address;
        ParticipantBic = original.ParticipantBic;
        IndirectParticipantBic = original.IndirectParticipantBic;
        Account = original.Account;
        Type = original.Type;
        Name = original.Name;
        Identifier = original.Identifier;
    }

    public Pacs008PostalAddressInput? Address { get; init; }
    public string? ParticipantBic { get; init; }
    public string? IndirectParticipantBic { get; init; }
    public string? Account { get; init; }

    public bool Equals(Pacs008CreditorInput? other) => other is not null &&
            Equals(Address, other.Address) &&
            Equals(ParticipantBic, other.ParticipantBic) &&
            Equals(IndirectParticipantBic, other.IndirectParticipantBic) &&
            Equals(Account, other.Account) &&
            Equals(Type, other.Type) &&
            Equals(Name, other.Name) &&
            Equals(Identifier, other.Identifier);
    public override bool Equals(object? obj) => obj is Pacs008CreditorInput other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Address);
        hash.Add(ParticipantBic);
        hash.Add(IndirectParticipantBic);
        hash.Add(Account);
        hash.Add(Type);
        hash.Add(Name);
        hash.Add(Identifier);
        return hash.ToHashCode();
    }

    public static bool operator ==(Pacs008CreditorInput? left, Pacs008CreditorInput? right) => Equals(left, right);
    public static bool operator !=(Pacs008CreditorInput? left, Pacs008CreditorInput? right) => !Equals(left, right);
}

public sealed class Pacs008UltimatePartyInput : Pacs008PartyInput, IEquatable<Pacs008UltimatePartyInput>
{
    public bool Equals(Pacs008UltimatePartyInput? other) => other is not null &&
            Equals(Type, other.Type) &&
            Equals(Name, other.Name) &&
            Equals(Identifier, other.Identifier);
    public override bool Equals(object? obj) => obj is Pacs008UltimatePartyInput other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Type);
        hash.Add(Name);
        hash.Add(Identifier);
        return hash.ToHashCode();
    }

    public static bool operator ==(Pacs008UltimatePartyInput? left, Pacs008UltimatePartyInput? right) => Equals(left, right);
    public static bool operator !=(Pacs008UltimatePartyInput? left, Pacs008UltimatePartyInput? right) => !Equals(left, right);
}

public sealed class Pacs008PostalAddressInput : IEquatable<Pacs008PostalAddressInput>
{
    public string? StreetName { get; init; }
    public string? BuildingNumber { get; init; }
    public string? PostCode { get; init; }
    public string? TownName { get; init; }
    public string? CountrySubdivision { get; init; }
    public string? Country { get; init; }
    public string? AddressLines { get; init; }

    public bool Equals(Pacs008PostalAddressInput? other) => other is not null &&
            Equals(StreetName, other.StreetName) &&
            Equals(BuildingNumber, other.BuildingNumber) &&
            Equals(PostCode, other.PostCode) &&
            Equals(TownName, other.TownName) &&
            Equals(CountrySubdivision, other.CountrySubdivision) &&
            Equals(Country, other.Country) &&
            Equals(AddressLines, other.AddressLines);
    public override bool Equals(object? obj) => obj is Pacs008PostalAddressInput other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StreetName);
        hash.Add(BuildingNumber);
        hash.Add(PostCode);
        hash.Add(TownName);
        hash.Add(CountrySubdivision);
        hash.Add(Country);
        hash.Add(AddressLines);
        return hash.ToHashCode();
    }

    public static bool operator ==(Pacs008PostalAddressInput? left, Pacs008PostalAddressInput? right) => Equals(left, right);
    public static bool operator !=(Pacs008PostalAddressInput? left, Pacs008PostalAddressInput? right) => !Equals(left, right);
}

public sealed class Pacs008PaymentInitiationInput : IEquatable<Pacs008PaymentInitiationInput>
{
    public Pacs008PaymentInitiationInput()
    {
    }

    public Pacs008PaymentInitiationInput(Pacs008PaymentInitiationInput original)
    {
        ChannelCode = original.ChannelCode;
        Geolocation = original.Geolocation;
    }

    public string? ChannelCode { get; init; }
    public IReadOnlyList<string>? Geolocation { get; init; }

    public bool Equals(Pacs008PaymentInitiationInput? other) => other is not null &&
            Equals(ChannelCode, other.ChannelCode) &&
            Equals(Geolocation, other.Geolocation);
    public override bool Equals(object? obj) => obj is Pacs008PaymentInitiationInput other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ChannelCode);
        hash.Add(Geolocation);
        return hash.ToHashCode();
    }

    public static bool operator ==(Pacs008PaymentInitiationInput? left, Pacs008PaymentInitiationInput? right) => Equals(left, right);
    public static bool operator !=(Pacs008PaymentInitiationInput? left, Pacs008PaymentInitiationInput? right) => !Equals(left, right);
}

public sealed class Pacs008InitiationChannelInstrumentInput : IEquatable<Pacs008InitiationChannelInstrumentInput>
{
    public Pacs008InitiationChannelInstrumentInput()
    {
    }

    public Pacs008InitiationChannelInstrumentInput(Pacs008InitiationChannelInstrumentInput original)
    {
        ChannelCode = original.ChannelCode;
        InstrumentCodes = original.InstrumentCodes;
        ElectronicAddress = original.ElectronicAddress;
    }

    public string? ChannelCode { get; init; }
    public IReadOnlyList<string>? InstrumentCodes { get; init; }
    public string? ElectronicAddress { get; init; }

    public bool Equals(Pacs008InitiationChannelInstrumentInput? other) => other is not null &&
            Equals(ChannelCode, other.ChannelCode) &&
            Equals(InstrumentCodes, other.InstrumentCodes) &&
            Equals(ElectronicAddress, other.ElectronicAddress);
    public override bool Equals(object? obj) => obj is Pacs008InitiationChannelInstrumentInput other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ChannelCode);
        hash.Add(InstrumentCodes);
        hash.Add(ElectronicAddress);
        return hash.ToHashCode();
    }

    public static bool operator ==(Pacs008InitiationChannelInstrumentInput? left, Pacs008InitiationChannelInstrumentInput? right) => Equals(left, right);
    public static bool operator !=(Pacs008InitiationChannelInstrumentInput? left, Pacs008InitiationChannelInstrumentInput? right) => !Equals(left, right);
}

public sealed class Pacs008RemittanceInput : IEquatable<Pacs008RemittanceInput>
{
    public Pacs008RemittanceInput()
    {
    }

    public Pacs008RemittanceInput(Pacs008RemittanceInput original)
    {
        Unstructured = original.Unstructured;
        Structured = original.Structured;
    }

    public string? Unstructured { get; init; }
    public IReadOnlyList<Pacs008StructuredRemittanceInput>? Structured { get; init; }

    public bool Equals(Pacs008RemittanceInput? other) => other is not null &&
            Equals(Unstructured, other.Unstructured) &&
            Equals(Structured, other.Structured);
    public override bool Equals(object? obj) => obj is Pacs008RemittanceInput other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Unstructured);
        hash.Add(Structured);
        return hash.ToHashCode();
    }

    public static bool operator ==(Pacs008RemittanceInput? left, Pacs008RemittanceInput? right) => Equals(left, right);
    public static bool operator !=(Pacs008RemittanceInput? left, Pacs008RemittanceInput? right) => !Equals(left, right);
}

public sealed class Pacs008StructuredRemittanceInput : IEquatable<Pacs008StructuredRemittanceInput>
{
    public string? ReferenceType { get; init; }
    public string? ReferenceIssuer { get; init; }
    public string? Reference { get; init; }
    public string? AdditionalInformation { get; init; }

    public bool Equals(Pacs008StructuredRemittanceInput? other) => other is not null &&
            Equals(ReferenceType, other.ReferenceType) &&
            Equals(ReferenceIssuer, other.ReferenceIssuer) &&
            Equals(Reference, other.Reference) &&
            Equals(AdditionalInformation, other.AdditionalInformation);
    public override bool Equals(object? obj) => obj is Pacs008StructuredRemittanceInput other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ReferenceType);
        hash.Add(ReferenceIssuer);
        hash.Add(Reference);
        hash.Add(AdditionalInformation);
        return hash.ToHashCode();
    }

    public static bool operator ==(Pacs008StructuredRemittanceInput? left, Pacs008StructuredRemittanceInput? right) => Equals(left, right);
    public static bool operator !=(Pacs008StructuredRemittanceInput? left, Pacs008StructuredRemittanceInput? right) => !Equals(left, right);
}

public abstract class Pacs008PartyInput
{
    public int? Type { get; init; }
    public string? Name { get; init; }
    public string? Identifier { get; init; }
}
