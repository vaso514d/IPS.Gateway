using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Payments.Pacs009;

namespace IPS.Middleware.Infrastructure.Inbound.Transfers;

// Reads a pacs.009 from IPS. Nothing in the message is trusted before its signature verifies; a transfer is delivered only
// when it is schema-valid, single, and names both agents by BICFI. Field rules follow the source mapper.
public sealed class IncomingPacs009Protocol(IpsSignatureTrust trust) : IIncomingTransferProtocol
{
    private static readonly XNamespace Pacs = Pacs009Xml.DocumentNamespace;

    public bool Reads(string messageType) => PaymentMessageTypes.IsPacs009(messageType);

    public IncomingTransferReadResult Read(string xml, DateTimeOffset receivedAtUtc)
    {
        try
        {
            var opened = SignedEnvelope.Open(xml, receivedAtUtc, trust, Pacs, PaymentMessageTypes.Pacs009Definition.Equals);
            if (opened.Document is not { } body)
            {
                return Hold(opened.HoldReason!);
            }

            var transfer = body.Element(Pacs + "FICdtTrf");
            var group = transfer?.Element(Pacs + "GrpHdr");
            var transactions = transfer?.Elements(Pacs + "CdtTrfTxInf").ToArray() ?? [];
            if (group is null || transactions.Length != 1 || group.Element(Pacs + "NbOfTxs")?.Value != "1")
            {
                return Hold("pacs.009 must contain exactly one credit transfer and NbOfTxs = 1.");
            }

            Pacs008Schema.ValidatePacs009(xml);
            return new IncomingTransferReadResult.Ready(Map(group, transactions[0]));
        }
        catch (UnsupportedContent unsupported)
        {
            return Hold(unsupported.Message);
        }
        catch (Exception error) when (error is XmlException or XmlSchemaException or FormatException or OverflowException or InvalidOperationException)
        {
            return Hold("Malformed or unsupported transfer content.");
        }
    }

    private static IncomingPacs009 Map(XElement group, XElement transaction)
    {
        var paymentId = transaction.Element(Pacs + "PmtId")!;
        var amount = transaction.Element(Pacs + "IntrBkSttlmAmt")!;
        var typeInformation = transaction.Element(Pacs + "PmtTpInf") ?? group.Element(Pacs + "PmtTpInf");
        var valueDate = transaction.Element(Pacs + "IntrBkSttlmDt")?.Value ?? group.Element(Pacs + "IntrBkSttlmDt")?.Value;
        var remittance = string.Concat(transaction.Element(Pacs + "RmtInf")?.Elements(Pacs + "Ustrd").Select(line => line.Value) ?? []);

        return new IncomingPacs009(
            MessageId: group.Element(Pacs + "MsgId")!.Value,
            EndToEndId: paymentId.Element(Pacs + "EndToEndId")!.Value,
            InstructionId: paymentId.Element(Pacs + "InstrId")?.Value,
            TransactionId: paymentId.Element(Pacs + "TxId")?.Value,
            Uetr: Guid.TryParse(paymentId.Element(Pacs + "UETR")?.Value, out var uetr) ? uetr : null,
            InstructionPriority: typeInformation?.Element(Pacs + "InstrPrty")?.Value switch { "HIGH" => 1, "NORM" => 0, _ => null },
            ValueDate: valueDate is null ? null : DateOnly.Parse(valueDate, CultureInfo.InvariantCulture),
            Currency: amount.Attribute("Ccy")!.Value,
            Amount: decimal.Parse(amount.Value, CultureInfo.InvariantCulture),
            DebtorAgent: Agent(transaction.Element(Pacs + "DbtrAgt") ?? transaction.Element(Pacs + "Dbtr"), "debtor"),
            CreditorAgent: Agent(transaction.Element(Pacs + "CdtrAgt") ?? transaction.Element(Pacs + "Cdtr"), "creditor"),
            CategoryPurpose: Code(typeInformation?.Element(Pacs + "CtgyPurp")),
            Purpose: Code(transaction.Element(Pacs + "Purp"))?.Value,
            AdditionalPurpose: remittance.Length == 0 ? null : remittance,
            DebtorAccount: Account(transaction.Element(Pacs + "DbtrAcct")),
            CreditorAccount: Account(transaction.Element(Pacs + "CdtrAcct")));
    }

    private static IncomingAgent Agent(XElement? agent, string role)
    {
        var institution = agent?.Element(Pacs + "FinInstnId");
        var bic = institution?.Element(Pacs + "BICFI")?.Value;
        return bic is null
            ? throw new UnsupportedContent($"The {role} agent of the transfer has no BICFI.")
            : new IncomingAgent(bic, institution!.Element(Pacs + "ClrSysMmbId")?.Element(Pacs + "MmbId")?.Value);
    }

    private static string? Account(XElement? account)
    {
        var id = account?.Element(Pacs + "Id");
        return id?.Element(Pacs + "IBAN")?.Value ?? id?.Element(Pacs + "Othr")?.Element(Pacs + "Id")?.Value;
    }

    private static IncomingCode? Code(XElement? choice) =>
        choice?.Element(Pacs + "Cd")?.Value is { } code ? new(0, code)
        : choice?.Element(Pacs + "Prtry")?.Value is { } proprietary ? new(1, proprietary)
        : null;

    private static IncomingTransferReadResult Hold(string reason) => new IncomingTransferReadResult.Hold(reason);

    private sealed class UnsupportedContent(string reason) : Exception(reason);
}
