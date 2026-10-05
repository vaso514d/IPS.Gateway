using System.Globalization;
using System.Xml.Linq;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Inbound.Pacs008;

internal static class IncomingPacs008Mapping
{
    private static readonly XNamespace Head = Pacs008Xml.HeaderNamespace;
    private static readonly XNamespace Pacs = Pacs008Xml.DocumentNamespace;
    internal static IncomingPacs008Reference Original(XElement header, XElement group, XElement transaction)
    {
        var id = Child(transaction, "PmtId");
        var type = PaymentType(group, transaction);
        var settlement = Value(transaction, "IntrBkSttlmDt") ?? Value(group, "IntrBkSttlmDt");
        return new(
            businessMessageId: Required(header.Element(Head + "BizMsgIdr")?.Value),
            groupMessageId: Required(Value(group, "MsgId")),
            endToEndId: Required(Value(id, "EndToEndId")),
            transactionId: Value(id, "TxId"),
            uetr: Guid.TryParse(Value(id, "UETR"), out var uetr) ? uetr : null,
            groupCreatedAtUtc: Date(Value(group, "CreDtTm")),
            settlementDate: settlement is null ? null : DateOnly.Parse(settlement, CultureInfo.InvariantCulture),
            debtorAgentBic: Agent(Child(transaction, "DbtrAgt")).Bic,
            serviceLevelCode: Value(Child(type, "SvcLvl"), "Cd"),
            localInstrumentCode: Value(Child(type, "LclInstrm"), "Cd"));
    }

    internal static Pacs008Request Payment(XElement group, XElement transaction)
    {
        var id = Child(transaction, "PmtId");
        var amount = Child(transaction, "IntrBkSttlmAmt")!;
        var type = PaymentType(group, transaction);
        return new()
        {
            InstructionId = Value(id, "InstrId"),
            EndToEndId = Required(Value(id, "EndToEndId")),
            CreationDateTime = Date(Value(group, "CreDtTm")),
            AcceptanceDateTime = Date(Value(transaction, "AccptncDtTm")),
            Amount = decimal.Parse(amount.Value, CultureInfo.InvariantCulture),
            Currency = (string?)amount.Attribute("Ccy"),
            InstructionPriority = Value(type, "InstrPrty")?.ToUpperInvariant(),
            CategoryPurposeCode = Value(Child(type, "CtgyPurp"), "Cd"),
            Debtor = Debtor(transaction),
            Creditor = Creditor(transaction),
            UltimateDebtor = Ultimate(Child(transaction, "UltmtDbtr")),
            UltimateCreditor = Ultimate(Child(transaction, "UltmtCdtr")),
            PaymentInitiation = Initiation(transaction),
            InitiationChannelInstrument = Instruments(transaction),
            Remittance = Remittance(Child(transaction, "RmtInf"))
        };
    }

    private static Pacs008DebtorInput? Debtor(XElement transaction) => Party(transaction, "Dbtr") is not { } party ? null : new()
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
    private static Pacs008CreditorInput? Creditor(XElement transaction) => Party(transaction, "Cdtr") is not { } party ? null : new()
    {
        Type = party.Type,
        Name = party.Name,
        Identifier = party.Identifier,
        Address = party.Address,
        ParticipantBic = party.Bic,
        IndirectParticipantBic = party.Member,
        Account = party.Account
    };
    // Debtor and creditor share one layout: party, account and agent, each optional.
    private static PartyFields? Party(XElement transaction, string role)
    {
        var party = Child(transaction, role);
        var account = Child(transaction, role + "Acct");
        var agent = Child(transaction, role + "Agt");
        if (party is null && account is null && agent is null)
        {
            return null;
        }

        var (type, identifier, bill) = Identity(party);
        var (bic, member) = Agent(agent);
        return new(type, Value(party, "Nm"), identifier, bill, Address(Child(party, "PstlAdr")), bic, member, Account(account));
    }

    private sealed class PartyFields
    {
        public PartyFields(
        int? type,
        string? name,
        string? identifier,
        string? bill,
        Pacs008PostalAddressInput? address,
        string? bic,
        string? member,
        string? account)
        {
            Type = type;
            Name = name;
            Identifier = identifier;
            Bill = bill;
            Address = address;
            Bic = bic;
            Member = member;
            Account = account;
        }

        public int? Type { get; init; }
        public string? Name { get; init; }
        public string? Identifier { get; init; }
        public string? Bill { get; init; }
        public Pacs008PostalAddressInput? Address { get; init; }
        public string? Bic { get; init; }
        public string? Member { get; init; }
        public string? Account { get; init; }
    }

    private static Pacs008UltimatePartyInput? Ultimate(XElement? party)
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

    private static (int? Type, string? Identifier, string? Bill) Identity(XElement? party)
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

    private static (string? Bic, string? Member) Agent(XElement? agent)
    {
        var institution = Child(agent, "FinInstnId");
        return (Value(institution, "BICFI"), Value(Child(institution, "ClrSysMmbId"), "MmbId"));
    }

    private static string? Account(XElement? account) => Value(Child(account, "Id"), "IBAN") ?? Value(Child(Child(account, "Id"), "Othr"), "Id");
    private static Pacs008PostalAddressInput? Address(XElement? address) => address is null ? null : new()
    {
        StreetName = Value(address, "StrtNm"),
        BuildingNumber = Value(address, "BldgNb"),
        PostCode = Value(address, "PstCd"),
        TownName = Value(address, "TwnNm"),
        CountrySubdivision = Value(address, "CtrySubDvsn"),
        Country = Value(address, "Ctry"),
        AddressLines = Join(Children(address, "AdrLine"))
    };
    private static Pacs008PaymentInitiationInput? Initiation(XElement transaction)
    {
        var details = Child(Child(transaction, "RgltryRptg"), "Dtls");
        if (details is null)
        {
            return null;
        }

        var location = Children(details, "Inf").Select(x => x.Value.Trim()).Where(x => x.Length > 0).ToArray();
        var channel = Value(details, "Cd");
        return channel is null && location.Length == 0 ? null : new()
        {
            ChannelCode = channel,
            Geolocation = location.Length == 0 ? null : location
        };
    }

    private static Pacs008InitiationChannelInstrumentInput? Instruments(XElement transaction)
    {
        string? channel = null, address = null;
        var instruments = new List<string>();
        foreach (var related in Children(transaction, "RltdRmtInf"))
        {
            var parts = (Value(related, "RmtId") ?? "").Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            channel ??= parts[0];
            foreach (var instrument in parts.Skip(1))
            {
                if (!instruments.Contains(instrument))
                {
                    instruments.Add(instrument);
                }
            }

            address ??= Value(Child(related, "RmtLctnDtls"), "ElctrncAdr");
        }

        return channel is null ? null : new()
        {
            ChannelCode = channel,
            InstrumentCodes = instruments,
            ElectronicAddress = address
        };
    }

    private static Pacs008RemittanceInput? Remittance(XElement? remittance)
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

    // Transaction-level payment type overrides the group default.
    private static XElement? PaymentType(XElement group, XElement transaction) => Child(transaction, "PmtTpInf") ?? Child(group, "PmtTpInf");
    private static XElement? Child(XElement? element, string name) => element?.Element(Pacs + name);
    private static IEnumerable<XElement> Children(XElement? element, string name) => element?.Elements(Pacs + name) ?? [];
    private static string? Value(XElement? element, string name) => Child(element, name)?.Value.Trim() is { Length: > 0 } value ? value : null;
    private static string Required(string? value) => string.IsNullOrWhiteSpace(value) ? throw new FormatException("Missing correlation.") : value.Trim();
    private static string? Join(IEnumerable<XElement> elements) => string.Concat(elements.Select(e => e.Value)) is { Length: > 0 } value ? value : null;
    private static DateTimeOffset? Date(string? value) => value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime();
}
