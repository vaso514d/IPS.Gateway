using System.Xml.Linq;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Inbound.Pacs008;

// Reads the parties, accounts, agents, addresses and remittance of an incoming message in the pacs.008 Bank/Core shape.
// A pacs.008 and a pain.001 lay these out alike, so one reader serves both with their own document namespace.
internal sealed class IncomingPartyReader(XNamespace ns)
{
    // The debtor, its account and its agent are the children of the parent that are named Dbtr, DbtrAcct and DbtrAgt.
    internal Pacs008DebtorInput? Debtor(XElement parent) => Party(parent, "Dbtr") is not { } party ? null : new()
    {
        Type = party.Type,
        Name = party.Name,
        Identifier = party.Identifier,
        BillIdentifier = party.Bill,
        Address = party.Address,
        ParticipantBic = party.Bic,
        IndirectParticipantBic = party.Member,
        Account = party.Account
    };

    internal Pacs008CreditorInput? Creditor(XElement parent) => Party(parent, "Cdtr") is not { } party ? null : new()
    {
        Type = party.Type,
        Name = party.Name,
        Identifier = party.Identifier,
        Address = party.Address,
        ParticipantBic = party.Bic,
        IndirectParticipantBic = party.Member,
        Account = party.Account
    };

    internal Pacs008UltimatePartyInput? Ultimate(XElement? party)
    {
        if (party is null)
        {
            return null;
        }

        var identity = Identity(party);
        return new()
        {
            Type = identity.Type,
            Name = Value(party, "Nm"),
            Identifier = identity.Identifier
        };
    }

    internal (string? Bic, string? Member) Agent(XElement? agent)
    {
        var institution = Child(agent, "FinInstnId");
        return (Value(institution, "BICFI"), Value(Child(institution, "ClrSysMmbId"), "MmbId"));
    }

    internal Pacs008RemittanceInput? Remittance(XElement? remittance)
    {
        var text = Join(Children(remittance, "Ustrd"));
        var references = Children(remittance, "Strd").Select(item =>
        {
            var reference = Child(item, "CdtrRefInf");
            var type = Child(reference, "Tp");
            var choice = Child(type, "CdOrPrtry");
            return new Pacs008StructuredRemittanceInput
            {
                ReferenceType = Value(choice, "Prtry") ?? Value(choice, "Cd"),
                ReferenceIssuer = Value(type, "Issr"),
                Reference = Value(reference, "Ref"),
                AdditionalInformation = Join(Children(item, "AddtlRmtInf"))
            };
        }).ToArray();
        return text is null && references.Length == 0 ? null : new()
        {
            Unstructured = text,
            Structured = references.Length == 0 ? null : references
        };
    }

    internal XElement? Child(XElement? element, string name) => element?.Element(ns + name);

    internal IEnumerable<XElement> Children(XElement? element, string name) => element?.Elements(ns + name) ?? [];

    internal string? Value(XElement? element, string name) => Child(element, name)?.Value.Trim() is { Length: > 0 } value ? value : null;

    // Debtor and creditor share one layout: party, account and agent, each optional.
    private PartyFields? Party(XElement parent, string role)
    {
        var party = Child(parent, role);
        var account = Child(parent, role + "Acct");
        var agent = Child(parent, role + "Agt");
        if (party is null && account is null && agent is null)
        {
            return null;
        }

        var (type, identifier, bill) = Identity(party);
        var (bic, member) = Agent(agent);
        return new(type, Value(party, "Nm"), identifier, bill, Address(Child(party, "PstlAdr")), bic, member, Account(account));
    }

    private sealed record PartyFields(
        int? Type,
        string? Name,
        string? Identifier,
        string? Bill,
        Pacs008PostalAddressInput? Address,
        string? Bic,
        string? Member,
        string? Account);

    private (int? Type, string? Identifier, string? Bill) Identity(XElement? party)
    {
        var id = Child(party, "Id");
        var organization = Child(id, "OrgId");
        var branch = organization ?? Child(id, "PrvtId");
        if (branch is null)
        {
            return (null, null, null);
        }

        string? identifier = null, bill = null;
        foreach (var other in Children(branch, "Othr"))
        {
            if (string.Equals(Value(Child(other, "SchmeNm"), "Cd"), "BILL", StringComparison.OrdinalIgnoreCase))
            {
                bill ??= Value(other, "Id");
            }
            else
            {
                identifier ??= Value(other, "Id");
            }
        }

        return (organization is null ? 1 : 0, identifier ?? Value(branch, "AnyBIC") ?? Value(branch, "LEI"), bill);
    }

    private string? Account(XElement? account) => Value(Child(account, "Id"), "IBAN") ?? Value(Child(Child(account, "Id"), "Othr"), "Id");

    private Pacs008PostalAddressInput? Address(XElement? address) => address is null ? null : new()
    {
        StreetName = Value(address, "StrtNm"),
        BuildingNumber = Value(address, "BldgNb"),
        PostCode = Value(address, "PstCd"),
        TownName = Value(address, "TwnNm"),
        CountrySubdivision = Value(address, "CtrySubDvsn"),
        Country = Value(address, "Ctry"),
        AddressLines = Join(Children(address, "AdrLine"))
    };

    private static string? Join(IEnumerable<XElement> elements) => string.Concat(elements.Select(e => e.Value)) is { Length: > 0 } value ? value : null;
}
