using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Recalls;
using IPS.Middleware.Infrastructure.Payments.Camt056;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Inbound.Transfers;

// Reads a recall request (camt.056) from IPS (Annex D 3.2.4, 8.1.5: one recalled transaction). Nothing in the message is
// trusted before its signature verifies; a recall is delivered only when it is schema-valid, single, carries the recalled
// payment's ids, amount and settlement date, and names both agents of that payment by BICFI.
public sealed class IncomingCamt056Protocol(IpsSignatureTrust trust) : IIncomingTransferProtocol
{
    private static readonly XNamespace Camt = Camt056Xml.DocumentNamespace;
    private static readonly RecallReferenceReader Reader = new(Camt);

    public bool Reads(string messageType) => PaymentMessageTypes.IsCamt056(messageType);

    public IncomingTransferReadResult Read(string xml, DateTimeOffset receivedAtUtc)
    {
        try
        {
            var opened = SignedEnvelope.Open(xml, receivedAtUtc, trust, Camt, PaymentMessageTypes.Camt056Definition.Equals);
            if (opened.Document is not { } body)
            {
                return Hold(opened.HoldReason!);
            }

            var request = body.Element(Camt + "FIToFIPmtCxlReq");
            var underlying = request?.Elements(Camt + "Undrlyg").ToArray() ?? [];
            var transactions = underlying.Length == 1 ? underlying[0].Elements(Camt + "TxInf").ToArray() : [];
            if (request is null || transactions.Length != 1)
            {
                return Hold("camt.056 must recall exactly one transaction.");
            }

            Pacs008Schema.ValidateCamt056(xml);
            return new IncomingTransferReadResult.Ready(Map(request.Element(Camt + "Assgnmt")!, transactions[0]).Frozen());
        }
        catch (UnsupportedRecallContent unsupported)
        {
            return Hold(unsupported.Message);
        }
        catch (Exception error) when (error is XmlException or XmlSchemaException or FormatException or OverflowException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return Hold("Malformed or unsupported recall content.");
        }
    }

    private static IncomingCamt056 Map(XElement assignment, XElement transaction)
    {
        var amount = Reader.Child(transaction, "OrgnlIntrBkSttlmAmt") ?? throw Missing("OrgnlIntrBkSttlmAmt");
        var reference = Reader.Child(transaction, "OrgnlTxRef") ?? throw Missing("OrgnlTxRef");
        return new IncomingCamt056(
            MessageId: Reader.Value(assignment, "Id")!,
            CreatedAt: RecallReferenceReader.Timestamp(Reader.Value(assignment, "CreDtTm")!),
            RecallId: Required(transaction, "CxlId"),
            OriginalMessageId: Reader.Value(Reader.Child(transaction, "OrgnlGrpInf"), "OrgnlMsgId"),
            OriginalEndToEndId: Required(transaction, "OrgnlEndToEndId"),
            OriginalTransactionId: Required(transaction, "OrgnlTxId"),
            OriginalAmount: RecallReferenceReader.Amount(amount),
            OriginalCurrency: amount.Attribute("Ccy")!.Value,
            OriginalSettlementDate: RecallReferenceReader.Date(Required(transaction, "OrgnlIntrBkSttlmDt"))!.Value,
            ReasonCode: Reader.Code(Reader.Child(Reader.Child(transaction, "CxlRsnInf"), "Rsn")),
            Original: Reader.Original<RecallOriginalInput>(reference));
    }

    private static string Required(XElement transaction, string name) => Reader.Value(transaction, name) ?? throw Missing(name);

    private static UnsupportedRecallContent Missing(string name) => new($"The recall has no {name}.");

    private static IncomingTransferReadResult Hold(string reason) => new IncomingTransferReadResult.Hold(reason);
}
