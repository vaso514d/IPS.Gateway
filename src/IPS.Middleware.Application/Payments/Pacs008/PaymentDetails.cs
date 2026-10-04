namespace IPS.Middleware.Application.Payments.Pacs008;

public enum PaymentPartyKind { Organisation = 0, Individual = 1 }
public enum PaymentAccountKind { Iban, Treasury }
public enum PaymentPriority { Normal, High }

public sealed record PaymentParty(PaymentPartyKind Kind, string Name, string? Identifier, string? BillIdentifier, PaymentAddress? Address);
public sealed record PaymentAccount(string Value, PaymentAccountKind Kind);
public sealed record PaymentAgent(string Bic, string? IndirectParticipant);
public sealed record PaymentAddress(string? StreetName, string? BuildingNumber, string? PostCode,
    string? TownName, string? CountrySubdivision, string? Country, string? AddressLines);
public sealed record PaymentInitiation(string? ChannelCode, IReadOnlyList<string> Geolocation);
public sealed record PaymentInitiationChannel(string ChannelCode, IReadOnlyList<string> InstrumentCodes, string? ElectronicAddress);
public sealed record PaymentRemittance(string? Unstructured, IReadOnlyList<PaymentRemittanceReference> Structured);
public sealed record PaymentRemittanceReference(string Type, string Reference, string? Issuer, string? AdditionalInformation);
