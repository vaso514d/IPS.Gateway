using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Camt029;
using IPS.Middleware.Infrastructure.Payments.Camt029;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Inbound.Transfers;

// Reads a creditor bank's answer (camt.029) to a recall from IPS (Annex D 3.2.5, 8.1.6: one answered transaction). Nothing
// in the message is trusted before its signature verifies; an answer is delivered only when it is schema-valid, single,
// a refusal (RJCR) both as the confirmation and as the transaction's status, carries the recalled payment's ids and names
// both agents of that payment by BICFI. Matching it to our recall is the registration's work.
public sealed class IncomingCamt029Protocol(IpsSignatureTrust trust) : IIncomingTransferProtocol
{
    // The only answer IPS forwards besides the pacs.004 that accepts a recall (Annex D 8.1.6).
    private const string Refused = "RJCR";

    private static readonly XNamespace Head = Pacs008Xml.HeaderNamespace;
    private static readonly XNamespace Camt = Camt029Xml.DocumentNamespace;
    private static readonly RecallReferenceReader Reader = new(Camt);

    public bool Reads(string messageType) => PaymentMessageTypes.IsCamt029(messageType);

    public IncomingTransferReadResult Read(string xml, DateTimeOffset receivedAtUtc)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), Pacs008Schema.SafeReader);
            var root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root;
            if (root is null || root.Name != "Message" || root.Elements().ToArray() is not [var header, var body] ||
                header.Name != Head + "AppHdr" || body.Name != Camt + "Document")
            {
                return Hold("Unexpected message envelope or version.");
            }

            if (header.Element(Head + "MsgDefIdr")?.Value != PaymentMessageTypes.Camt029Definition)
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

            // Every transaction in the message counts, including one nested in an original payment information block
            // (OrgnlPmtInfAndSts), so a second answer cannot hide there; the one supported is the direct transaction of the
            // only cancellation details, as Annex D 3.2.5 shows it.
            var resolution = body.Element(Camt + "RsltnOfInvstgtn");
            var details = resolution?.Elements(Camt + "CxlDtls").ToArray() ?? [];
            var transactions = resolution?.Descendants(Camt + "TxInfAndSts").ToArray() ?? [];
            if (resolution is null || details is not [var detail] || transactions is not [var transaction] || transaction.Parent != detail)
            {
                return Hold("camt.029 must answer exactly one transaction.");
            }

            if (!IsRefusal(resolution, transaction))
            {
                return Hold("Only a refusal (RJCR) of a recall is supported.");
            }

            Pacs008Schema.ValidateCamt029(xml);
            return new IncomingTransferReadResult.Ready(Map(resolution.Element(Camt + "Assgnmt")!, transaction).Frozen());
        }
        catch (UnsupportedRecallContent unsupported)
        {
            return Hold(unsupported.Message);
        }
        catch (Exception error) when (error is XmlException or XmlSchemaException or FormatException or OverflowException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return Hold("Malformed or unsupported recall answer content.");
        }
    }

    private static bool IsRefusal(XElement resolution, XElement transaction) =>
        Reader.Value(Reader.Child(resolution, "Sts"), "Conf") == Refused
        && Reader.Value(transaction, "TxCxlSts") == Refused;

    private static IncomingCamt029 Map(XElement assignment, XElement transaction)
    {
        var reason = Reader.Child(transaction, "CxlStsRsnInf");
        var reference = Reader.Child(transaction, "OrgnlTxRef") ?? throw Missing("OrgnlTxRef");
        var amount = Reader.Child(reference, "IntrBkSttlmAmt");
        return new IncomingCamt029(
            MessageId: Reader.Value(assignment, "Id")!,
            CreatedAt: RecallReferenceReader.Timestamp(Reader.Value(assignment, "CreDtTm")!),
            CancellationStatusId: Required(transaction, "CxlStsId"),
            OriginalMessageId: Reader.Value(Reader.Child(transaction, "OrgnlGrpInf"), "OrgnlMsgId"),
            OriginalEndToEndId: Required(transaction, "OrgnlEndToEndId"),
            OriginalTransactionId: Required(transaction, "OrgnlTxId"),
            ReasonCode: Reader.Code(Reader.Child(reason, "Rsn")),
            AdditionalInformation: Reader.Value(reason, "AddtlInf"),
            Original: Reader.Original<Camt029OriginalInput>(reference) with
            {
                Currency = amount?.Attribute("Ccy")?.Value,
                Amount = amount is null ? null : RecallReferenceReader.Amount(amount)
            });
    }

    private static string Required(XElement transaction, string name) => Reader.Value(transaction, name) ?? throw Missing(name);

    private static UnsupportedRecallContent Missing(string name) => new($"The recall answer has no {name}.");

    private static IncomingTransferReadResult Hold(string reason) => new IncomingTransferReadResult.Hold(reason);
}
