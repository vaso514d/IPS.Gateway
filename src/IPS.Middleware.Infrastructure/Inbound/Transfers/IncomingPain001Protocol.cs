using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Inbound.Transfers;

// Reads a pain.001 payment initiation from IPS (Annex D 8.1.11: one PmtInf with one CdtTrfTxInf). Nothing in the message
// is trusted before its signature verifies; an initiation is delivered only when it is schema-valid, single, and carries
// a short enough initiation id, its message id and creation time, the debtor's agent (which must be us) and an instructed
// amount. Parties, accounts, agents, addresses and remittance are read as for a pacs.008.
public sealed class IncomingPain001Protocol(IpsSignatureTrust trust) : IIncomingTransferProtocol
{
    public const string DocumentNamespace = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.12";

    private static readonly XNamespace Head = Pacs008Xml.HeaderNamespace;
    private static readonly XNamespace Pain = DocumentNamespace;
    private static readonly IncomingPartyReader Parties = new(Pain);

    public bool Reads(string messageType) => PaymentMessageTypes.IsPain001(messageType);

    public IncomingTransferReadResult Read(string xml, DateTimeOffset receivedAtUtc)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), Pacs008Schema.SafeReader);
            var root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root;
            if (root is null || root.Name != "Message" || root.Elements().ToArray() is not [var header, var body] ||
                header.Name != Head + "AppHdr" || body.Name != Pain + "Document")
            {
                return Hold("Unexpected message envelope or version.");
            }

            if (!PaymentMessageTypes.IsPain001Definition(header.Element(Head + "MsgDefIdr")?.Value ?? string.Empty))
            {
                return Hold("Unsupported message definition.");
            }

            var signature = trust.Check(xml, receivedAtUtc);
            if (signature is IpsSignatureCheck.OutsideValidity outside)
            {
                return Hold($"IPS certificate outside its validity period: {outside.Detail}");
            }

            if (signature is not IpsSignatureCheck.Trusted)
            {
                return Hold("Untrusted message signature.");
            }

            var initiation = body.Element(Pain + "CstmrCdtTrfInitn");
            var group = initiation?.Element(Pain + "GrpHdr");
            var payments = initiation?.Elements(Pain + "PmtInf").ToArray() ?? [];
            if (group is null || payments.Length != 1 || group.Element(Pain + "NbOfTxs")?.Value != "1" ||
                payments[0].Elements(Pain + "CdtTrfTxInf").Count() != 1)
            {
                return Hold("pain.001 must contain exactly one payment instruction and NbOfTxs = 1.");
            }

            Pacs008Schema.ValidatePain001(xml);
            return new IncomingTransferReadResult.Ready(Map(group, payments[0]).Frozen());
        }
        catch (UnsupportedContent unsupported)
        {
            return Hold(unsupported.Message);
        }
        catch (Exception error) when (error is XmlException or XmlSchemaException or FormatException or OverflowException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return Hold("Malformed or unsupported initiation content.");
        }
    }

    private static IncomingPain001 Map(XElement group, XElement payment)
    {
        var transaction = payment.Element(Pain + "CdtTrfTxInf")!;
        var paymentInformationId = Required(Parties.Value(payment, "PmtInfId"), "PmtInfId");
        if (paymentInformationId.Length > IncomingPain001.MaxPaymentInformationIdLength)
        {
            throw new UnsupportedContent("The initiation id is longer than 31 characters, so it cannot be accepted with a PSP- payment.");
        }

        var debtor = Parties.Debtor(payment);
        if (debtor?.ParticipantBic is null)
        {
            throw new UnsupportedContent("The debtor agent of the initiation has no BICFI.");
        }

        var amount = Parties.Child(Parties.Child(transaction, "Amt"), "InstdAmt")
            ?? throw new UnsupportedContent("The initiation has no instructed amount.");
        var type = Parties.Child(payment, "PmtTpInf");
        var paymentId = Parties.Child(transaction, "PmtId");
        var party = Parties.Child(group, "InitgPty");
        return new IncomingPain001(
            MessageId: Required(Parties.Value(group, "MsgId"), "MsgId"),
            // Kept with its own offset: the accepting pacs.008 repeats it as the acceptance time.
            CreatedAt: DateTimeOffset.Parse(Required(Parties.Value(group, "CreDtTm"), "CreDtTm"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
            InitiatingParty: party is null
                ? null
                : new IncomingInitiatingParty(Parties.Value(party, "Nm"), Parties.Value(Parties.Child(Parties.Child(party, "Id"), "OrgId"), "AnyBIC")),
            PaymentInformationId: paymentInformationId,
            ServiceLevelCode: Parties.Value(Parties.Child(type, "SvcLvl"), "Cd"),
            LocalInstrumentCode: Parties.Value(Parties.Child(type, "LclInstrm"), "Cd"),
            CategoryPurposeCode: Parties.Value(Parties.Child(type, "CtgyPurp"), "Cd"),
            RequestedExecutionDate: RequestedExecutionDate.From(Parties.Child(payment, "ReqdExctnDt")),
            Debtor: debtor,
            UltimateDebtor: Parties.Ultimate(Parties.Child(payment, "UltmtDbtr")),
            InstructionId: Parties.Value(paymentId, "InstrId"),
            EndToEndId: Parties.Value(paymentId, "EndToEndId"),
            Amount: decimal.Parse(amount.Value, CultureInfo.InvariantCulture),
            Currency: amount.Attribute("Ccy")!.Value,
            Creditor: Parties.Creditor(transaction),
            UltimateCreditor: Parties.Ultimate(Parties.Child(transaction, "UltmtCdtr")),
            PurposeCode: Parties.Value(Parties.Child(transaction, "Purp"), "Cd"),
            Remittance: Parties.Remittance(Parties.Child(transaction, "RmtInf")));
    }

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new UnsupportedContent($"The initiation has no {name}.") : value.Trim();

    private static IncomingTransferReadResult Hold(string reason) => new IncomingTransferReadResult.Hold(reason);

    private sealed class UnsupportedContent(string reason) : Exception(reason);
}
