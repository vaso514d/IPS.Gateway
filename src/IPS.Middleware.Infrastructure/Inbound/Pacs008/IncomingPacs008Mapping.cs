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
    private static readonly IncomingPartyReader Parties = new(Pacs);

    internal static IncomingPacs008Reference Original(XElement header, XElement group, XElement transaction)
    {
        var id = Child(transaction, "PmtId");
        var type = PaymentType(group, transaction);
        var settlement = Value(transaction, "IntrBkSttlmDt") ?? Value(group, "IntrBkSttlmDt");
        return new(
            BusinessMessageId: Required(header.Element(Head + "BizMsgIdr")?.Value),
            GroupMessageId: Required(Value(group, "MsgId")),
            EndToEndId: Required(Value(id, "EndToEndId")),
            TransactionId: Value(id, "TxId"),
            Uetr: Guid.TryParse(Value(id, "UETR"), out var uetr) ? uetr : null,
            GroupCreatedAtUtc: Date(Value(group, "CreDtTm")),
            SettlementDate: settlement is null ? null : DateOnly.Parse(settlement, CultureInfo.InvariantCulture),
            DebtorAgentBic: Parties.Agent(Child(transaction, "DbtrAgt")).Bic,
            ServiceLevelCode: Value(Child(type, "SvcLvl"), "Cd"),
            LocalInstrumentCode: Value(Child(type, "LclInstrm"), "Cd"));
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
            Debtor = Parties.Debtor(transaction),
            Creditor = Parties.Creditor(transaction),
            UltimateDebtor = Parties.Ultimate(Child(transaction, "UltmtDbtr")),
            UltimateCreditor = Parties.Ultimate(Child(transaction, "UltmtCdtr")),
            PaymentInitiation = Initiation(transaction),
            InitiationChannelInstrument = Instruments(transaction),
            Remittance = Parties.Remittance(Child(transaction, "RmtInf"))
        };
    }

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

    // Transaction-level payment type overrides the group default.
    private static XElement? PaymentType(XElement group, XElement transaction) => Child(transaction, "PmtTpInf") ?? Child(group, "PmtTpInf");
    private static XElement? Child(XElement? element, string name) => Parties.Child(element, name);
    private static IEnumerable<XElement> Children(XElement? element, string name) => Parties.Children(element, name);
    private static string? Value(XElement? element, string name) => Parties.Value(element, name);
    private static string Required(string? value) => string.IsNullOrWhiteSpace(value) ? throw new FormatException("Missing correlation.") : value.Trim();
    private static DateTimeOffset? Date(string? value) => value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime();
}
