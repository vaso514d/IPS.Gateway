using System.Globalization;
using System.Xml.Linq;
using IPS.Middleware.Application.Payments.Recalls;

namespace IPS.Middleware.Infrastructure.Inbound.Transfers;

// Reads the parts the incoming camt.056, camt.029 and camt.055 share (Annex D 8.1.5, 8.1.6, 8.1.13) into the shapes our own
// recalls use. Values are kept as received; an absent part stays absent. Both agents of a quoted payment must be named by
// BICFI, because one of them must be us.
internal sealed class RecallReferenceReader(XNamespace ns)
{
    // The recalled payment (OrgnlTxRef). A camt.029 also quotes its amount, so the caller chooses the shape.
    internal T Original<T>(XElement reference)
        where T : RecallOriginalInput, new()
    {
        return new T
        {
            SettlementDate = Date(Value(reference, "IntrBkSttlmDt")),
            Remittance = Remittance(Child(reference, "RmtInf")),
            UltimateDebtor = Ultimate(Child(reference, "UltmtDbtr")),
            Debtor = Party(Child(reference, "Dbtr"), Child(reference, "DbtrAcct")),
            DebtorAgent = Agent(Child(reference, "DbtrAgt"), "debtor"),
            CreditorAgent = Agent(Child(reference, "CdtrAgt"), "creditor"),
            Creditor = Party(Child(reference, "Cdtr"), Child(reference, "CdtrAcct")),
            UltimateCreditor = Ultimate(Child(reference, "UltmtCdtr"))
        };
    }

    internal RecallAgentInput Agent(XElement? agent, string role)
    {
        var institution = Child(agent, "FinInstnId");
        var bic = Value(institution, "BICFI") ?? throw new UnsupportedRecallContent($"The {role} agent has no BICFI.");
        return new RecallAgentInput { Bic = bic, Name = Value(institution, "Nm") };
    }

    internal RecallAddressInput? Address(XElement? address)
    {
        if (address is null)
        {
            return null;
        }

        var lines = address.Elements(ns + "AdrLine")
            .Select(line => line.Value)
            .ToArray();
        return new RecallAddressInput
        {
            StreetName = Value(address, "StrtNm"),
            BuildingNumber = Value(address, "BldgNb"),
            PostCode = Value(address, "PstCd"),
            TownName = Value(address, "TwnNm"),
            CountrySubdivision = Value(address, "CtrySubDvsn"),
            Country = Value(address, "Ctry"),
            AddressLines = lines.Length == 0 ? null : lines
        };
    }

    // Type 0 is an organisation and type 1 an individual; a type is only given with an identifier.
    internal (int? Type, string? Identifier) Identification(XElement? party)
    {
        var identification = Child(party, "Id");
        var organisation = Child(identification, "OrgId");
        var individual = Child(identification, "PrvtId");
        var identifier = Value(Child(organisation ?? individual, "Othr"), "Id");
        return identifier is null ? (null, null) : (organisation is not null ? 0 : 1, identifier);
    }

    // Multiple Ustrd lines are joined, as for the other incoming types.
    internal string? Unstructured(XElement? remittance)
    {
        var text = string.Concat(remittance?.Elements(ns + "Ustrd").Select(line => line.Value) ?? []);
        return text.Length == 0 ? null : text;
    }

    internal string? Account(XElement? account)
    {
        var id = Child(account, "Id");
        return Value(id, "IBAN") ?? Value(Child(id, "Othr"), "Id");
    }

    internal XElement? Child(XElement? parent, string name) => parent?.Element(ns + name);

    internal string? Value(XElement? parent, string name) => Child(parent, name)?.Value;

    // The choice of an ISO code or a proprietary value; either is passed on as received.
    internal string? Code(XElement? choice) => Value(choice, "Cd") ?? Value(choice, "Prtry");

    internal static DateOnly? Date(string? value) => value is null ? null : DateOnly.Parse(value, CultureInfo.InvariantCulture);

    internal static DateTimeOffset Timestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    internal static decimal Amount(XElement amount) => decimal.Parse(amount.Value, CultureInfo.InvariantCulture);

    private RecallRemittanceInput? Remittance(XElement? remittance)
    {
        var reference = Child(Child(remittance, "Strd"), "CdtrRefInf");
        var unstructured = Unstructured(remittance);
        if (unstructured is null && reference is null)
        {
            return null;
        }

        return new RecallRemittanceInput
        {
            Unstructured = unstructured,
            CreditorReference = reference is null
                ? null
                : new RecallCreditorReferenceInput { Issuer = Value(Child(reference, "Tp"), "Issr"), Reference = Value(reference, "Ref") }
        };
    }

    private RecallPartyInput? Party(XElement? choice, XElement? account)
    {
        var party = Child(choice, "Pty");
        var number = Account(account);
        if (party is null && number is null)
        {
            return null;
        }

        var (type, identifier) = Identification(party);
        return new RecallPartyInput
        {
            Name = Value(party, "Nm"),
            Address = Address(Child(party, "PstlAdr")),
            Type = type,
            Identifier = identifier,
            Account = number
        };
    }

    private RecallUltimatePartyInput? Ultimate(XElement? choice)
    {
        var party = Child(choice, "Pty");
        if (party is null)
        {
            return null;
        }

        var (type, identifier) = Identification(party);
        return new RecallUltimatePartyInput { Name = Value(party, "Nm"), Type = type, Identifier = identifier };
    }
}

// Content the core system could not be given: held with this reason.
internal sealed class UnsupportedRecallContent(string reason) : Exception(reason);
