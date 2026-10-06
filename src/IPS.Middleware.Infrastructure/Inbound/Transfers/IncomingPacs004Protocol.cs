using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Infrastructure.Payments.Pacs004;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Inbound.Transfers;

// Reads a pacs.004 payment return from IPS. Nothing in the message is trusted before its signature verifies; a return is
// delivered only when it is schema-valid, single, carries its return id (equal to the group message id) and the original
// transaction id, and names both agents by BICFI. Field rules follow the source mapper and the profile of the outgoing
// return builder.
public sealed class IncomingPacs004Protocol(IReadOnlyCollection<X509Certificate2> trustedIpsCertificates) : IIncomingTransferProtocol
{
    private static readonly XNamespace Head = Pacs008Xml.HeaderNamespace;
    private static readonly XNamespace Pacs = Pacs004Xml.DocumentNamespace;

    public bool Reads(string messageType) => PaymentMessageTypes.IsPacs004(messageType);

    public IncomingTransferReadResult Read(string xml)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), Pacs008Schema.SafeReader);
            var root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root;
            if (root is null || root.Name != "Message" || root.Elements().ToArray() is not [var header, var body] ||
                header.Name != Head + "AppHdr" || body.Name != Pacs + "Document")
            {
                return Hold("Unexpected message envelope or version.");
            }

            if (header.Element(Head + "MsgDefIdr")?.Value != PaymentMessageTypes.Pacs004Definition)
            {
                return Hold("Unsupported message definition.");
            }

            if (!IpsSignatureVerifier.IsTrusted(xml, trustedIpsCertificates))
            {
                return Hold("Untrusted message signature.");
            }

            var payment = body.Element(Pacs + "PmtRtr");
            var group = payment?.Element(Pacs + "GrpHdr");
            var transactions = payment?.Elements(Pacs + "TxInf").ToArray() ?? [];
            if (group is null || transactions.Length != 1 || group.Element(Pacs + "NbOfTxs")?.Value != "1")
            {
                return Hold("pacs.004 must contain exactly one returned transaction and NbOfTxs = 1.");
            }

            Pacs008Schema.ValidatePacs004(xml);
            return new IncomingTransferReadResult.Ready(Map(group, transactions[0]));
        }
        catch (UnsupportedContent unsupported)
        {
            return Hold(unsupported.Message);
        }
        catch (Exception error) when (error is XmlException or XmlSchemaException or FormatException or OverflowException or InvalidOperationException)
        {
            return Hold("Malformed or unsupported return content.");
        }
    }

    private static IncomingPacs004 Map(XElement group, XElement transaction)
    {
        var returnId = Required(transaction.Element(Pacs + "RtrId")?.Value, "RtrId");
        if (group.Element(Pacs + "MsgId")?.Value != returnId)
        {
            throw new UnsupportedContent("The return id differs from the group message id.");
        }

        var amount = transaction.Element(Pacs + "RtrdIntrBkSttlmAmt")!;
        var reference = transaction.Element(Pacs + "OrgnlTxRef");
        var reason = transaction.Element(Pacs + "RtrRsnInf");
        var valueDate = transaction.Element(Pacs + "IntrBkSttlmDt")?.Value ?? group.Element(Pacs + "IntrBkSttlmDt")?.Value;
        var instructed = group.Element(Pacs + "InstdAgt") ?? reference?.Element(Pacs + "DbtrAgt");
        var instructing = group.Element(Pacs + "InstgAgt") ?? reference?.Element(Pacs + "CdtrAgt");
        var sender = reference?.Element(Pacs + "CdtrAgt")?.Element(Pacs + "FinInstnId");

        return new IncomingPacs004(
            ReturnId: returnId,
            Amount: decimal.Parse(amount.Value, CultureInfo.InvariantCulture),
            Currency: amount.Attribute("Ccy")!.Value,
            ValueDate: Date(valueDate),
            ReturnReasonCode: reason?.Element(Pacs + "Rsn")?.Element(Pacs + "Cd")?.Value ?? reason?.Element(Pacs + "Rsn")?.Element(Pacs + "Prtry")?.Value,
            AdditionalInformation: reason?.Element(Pacs + "AddtlInf")?.Value,
            InstructedAgent: Bic(instructed, "instructed"),
            InstructingAgent: Bic(instructing, "instructing"),
            SenderMemberId: sender?.Element(Pacs + "ClrSysMmbId")?.Element(Pacs + "MmbId")?.Value,
            Debtor: Party(reference?.Element(Pacs + "Dbtr"), reference?.Element(Pacs + "DbtrAcct")),
            Creditor: Party(reference?.Element(Pacs + "Cdtr"), reference?.Element(Pacs + "CdtrAcct")),
            Original: Original(transaction, reference));
    }

    private static IncomingOriginalPayment Original(XElement transaction, XElement? reference)
    {
        var group = transaction.Element(Pacs + "OrgnlGrpInf");
        var amount = transaction.Element(Pacs + "OrgnlIntrBkSttlmAmt");
        var date = reference?.Element(Pacs + "IntrBkSttlmDt")?.Value ?? transaction.Element(Pacs + "OrgnlIntrBkSttlmDt")?.Value;
        var created = group?.Element(Pacs + "OrgnlCreDtTm")?.Value;
        return new IncomingOriginalPayment(
            TransactionId: Required(transaction.Element(Pacs + "OrgnlTxId")?.Value, "OrgnlTxId"),
            InstructionId: transaction.Element(Pacs + "OrgnlInstrId")?.Value,
            EndToEndId: transaction.Element(Pacs + "OrgnlEndToEndId")?.Value,
            Uetr: Guid.TryParse(transaction.Element(Pacs + "OrgnlUETR")?.Value, out var uetr) ? uetr : null,
            ValueDate: Date(date),
            MessageId: group?.Element(Pacs + "OrgnlMsgId")?.Value,
            MessageNameId: group?.Element(Pacs + "OrgnlMsgNmId")?.Value,
            CreationDateTime: created is null ? null : DateTimeOffset.Parse(created, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
            Amount: amount is null ? null : decimal.Parse(amount.Value, CultureInfo.InvariantCulture),
            Currency: amount?.Attribute("Ccy")?.Value);
    }

    private static IncomingReturnParty? Party(XElement? party, XElement? account)
    {
        var person = party?.Element(Pacs + "Pty");
        var identification = person?.Element(Pacs + "Id");
        var organisation = identification?.Element(Pacs + "OrgId");
        var individual = identification?.Element(Pacs + "PrvtId");
        var identifier = (organisation ?? individual)?.Element(Pacs + "Othr")?.Element(Pacs + "Id")?.Value;
        var accountId = account?.Element(Pacs + "Id");
        var number = accountId?.Element(Pacs + "IBAN")?.Value ?? accountId?.Element(Pacs + "Othr")?.Element(Pacs + "Id")?.Value;
        var name = person?.Element(Pacs + "Nm")?.Value;
        if (name is null && identifier is null && number is null)
        {
            return null;
        }

        // Type 0 is an organisation and type 1 an individual, and a type is only given with an identifier.
        int? type = identifier is null ? null : organisation is not null ? 0 : 1;
        return new IncomingReturnParty(name, type, identifier, number);
    }

    private static string Bic(XElement? agent, string role) =>
        agent?.Element(Pacs + "FinInstnId")?.Element(Pacs + "BICFI")?.Value
        ?? throw new UnsupportedContent($"The {role} agent of the return has no BICFI.");

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new UnsupportedContent($"The return has no {name}.") : value.Trim();

    private static DateOnly? Date(string? value) => value is null ? null : DateOnly.Parse(value, CultureInfo.InvariantCulture);

    private static IncomingTransferReadResult Hold(string reason) => new IncomingTransferReadResult.Hold(reason);

    private sealed class UnsupportedContent(string reason) : Exception(reason);
}
