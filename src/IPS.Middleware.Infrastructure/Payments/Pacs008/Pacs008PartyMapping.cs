using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.XmlModels;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008;

internal static class Pacs008PartyMapping
{
    internal static PartyXml Party(PaymentParty party) => new()
    {
        Name = party.Name,
        Address = Address(party.Address),
        Identification = Identification(party)
    };

    private static PartyIdXml? Identification(PaymentParty party)
    {
        var identifiers = new List<IdentifierXml>();
        if (party.Identifier is { } identifier)
            identifiers.Add(new() { Value = identifier });
        if (party.BillIdentifier is { } bill)
            identifiers.Add(new() { Value = bill, Scheme = new() { Code = Pacs008ProtocolProfile.BillIdentificationScheme } });
        if (identifiers.Count == 0) return null;
        var values = new PartyIdentifiersXml { Other = identifiers.ToArray() };
        return party.Kind == PaymentPartyKind.Organisation
            ? new() { Organisation = values }
            : new() { Individual = values };
    }

    internal static AccountXml Account(PaymentAccount account) => new()
    {
        Identification = account.Kind == PaymentAccountKind.Treasury
            ? new() { Other = new() { Value = account.Value } }
            : new() { Iban = account.Value }
    };

    internal static AgentXml Agent(PaymentAgent agent) => new()
    {
        Identification = new()
        {
            Bic = agent.Bic,
            ClearingMember = agent.IndirectParticipant is { } member ? new()
            {
                ClearingSystem = new() { Code = Pacs008ProtocolProfile.IndirectClearingSystem },
                MemberId = member
            } : null
        }
    };

    private static AddressXml? Address(PaymentAddress? address)
    {
        if (address is null) return null;
        // Source profile splits raw address text before trimming individual lines.
        var lines = ProtocolTextChunks.Split(address.AddressLines, ProtocolTextChunks.AddressLineLength)
            .Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToArray();
        if (address.StreetName is null && address.BuildingNumber is null && address.PostCode is null &&
            address.TownName is null && address.CountrySubdivision is null && address.Country is null && lines.Length == 0)
            return null;
        return new()
        {
            Street = address.StreetName,
            Building = address.BuildingNumber,
            PostCode = address.PostCode,
            Town = address.TownName,
            Subdivision = address.CountrySubdivision,
            Country = address.Country,
            Lines = lines
        };
    }
}
