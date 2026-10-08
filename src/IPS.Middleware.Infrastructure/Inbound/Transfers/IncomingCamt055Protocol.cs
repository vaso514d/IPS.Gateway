using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Inbound.Transfers;

// Reads a PISP's cancellation request (camt.055.001.12) from IPS (Annex D 3.2.13, 8.1.13: one cancelled initiation with one
// transaction). Nothing in the message is trusted before its signature verifies; a request is delivered only when it is
// schema-valid, single, and names the debtor's agent (which must be us) by BICFI. The older camt.055.001.08 is not read.
public sealed class IncomingCamt055Protocol(IpsSignatureTrust trust) : IIncomingTransferProtocol
{
    public const string DocumentNamespace = "urn:iso:std:iso:20022:tech:xsd:camt.055.001.12";

    private static readonly XNamespace Camt = DocumentNamespace;
    private static readonly RecallReferenceReader Reader = new(Camt);

    public bool Reads(string messageType) => PaymentMessageTypes.IsCamt055(messageType);

    public IncomingTransferReadResult Read(string xml, DateTimeOffset receivedAtUtc)
    {
        try
        {
            var opened = SignedEnvelope.Open(xml, receivedAtUtc, trust, Camt, PaymentMessageTypes.IsCamt055Definition);
            if (opened.Document is not { } body)
            {
                return Hold(opened.HoldReason!);
            }

            var request = body.Element(Camt + "CstmrPmtCxlReq");
            var underlying = request?.Elements(Camt + "Undrlyg").ToArray() ?? [];
            var initiations = underlying.Length == 1 ? underlying[0].Elements(Camt + "OrgnlPmtInfAndCxl").ToArray() : [];
            if (request is null || initiations.Length != 1 || initiations[0].Elements(Camt + "TxInf").Count() != 1)
            {
                return Hold("camt.055 must cancel exactly one payment instruction with one transaction.");
            }

            Pacs008Schema.ValidateCamt055(xml);
            return new IncomingTransferReadResult.Ready(Map(request.Element(Camt + "Assgnmt")!, initiations[0]).Frozen());
        }
        catch (UnsupportedRecallContent unsupported)
        {
            return Hold(unsupported.Message);
        }
        catch (Exception error) when (error is XmlException or XmlSchemaException or FormatException or OverflowException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return Hold("Malformed or unsupported cancellation content.");
        }
    }

    private static IncomingCamt055 Map(XElement assignment, XElement initiation)
    {
        var transaction = initiation.Element(Camt + "TxInf")!;
        var group = Reader.Child(initiation, "OrgnlGrpInf");
        var created = Reader.Value(group, "OrgnlCreDtTm");
        // The reason belongs to the transaction (Annex D 8.1.13); one stated for the whole instruction is taken otherwise.
        var reason = Reader.Child(transaction, "CxlRsnInf") ?? Reader.Child(initiation, "CxlRsnInf");
        var originator = Reader.Child(reason, "Orgtr");
        return new IncomingCamt055(
            MessageId: Reader.Value(assignment, "Id")!,
            AssignerBic: AssignmentBic(assignment, "Assgnr"),
            AssigneeBic: AssignmentBic(assignment, "Assgne"),
            CreatedAt: RecallReferenceReader.Timestamp(Reader.Value(assignment, "CreDtTm")!),
            PaymentCancellationId: Reader.Value(initiation, "PmtCxlId"),
            OriginalPaymentInformationId: Reader.Value(initiation, "OrgnlPmtInfId")!,
            OriginalGroup: group is null
                ? null
                : new IncomingCancelledGroup(
                    Reader.Value(group, "OrgnlMsgId")!,
                    Reader.Value(group, "OrgnlMsgNmId")!,
                    created is null ? null : RecallReferenceReader.Timestamp(created)),
            CancellationId: Reader.Value(transaction, "CxlId"),
            OriginalInstructionId: Reader.Value(transaction, "OrgnlInstrId"),
            OriginalEndToEndId: Reader.Value(transaction, "OrgnlEndToEndId"),
            Reason: reason is null
                ? null
                : new IncomingCancellationReason(
                    Reader.Value(originator, "Nm"),
                    Reader.Value(Reader.Child(Reader.Child(originator, "Id"), "OrgId"), "AnyBIC"),
                    Reader.Code(Reader.Child(reason, "Rsn")),
                    Reader.Value(reason, "AddtlInf")),
            Original: Original(Reader.Child(transaction, "OrgnlTxRef")));
    }

    private static IncomingCancelledInitiation Original(XElement? reference)
    {
        var amount = Reader.Child(Reader.Child(reference, "Amt"), "InstdAmt");
        var type = Reader.Child(reference, "PmtTpInf");
        var creditor = Reader.Child(Reader.Child(reference, "Cdtr"), "Pty");
        return new IncomingCancelledInitiation(
            Currency: amount?.Attribute("Ccy")?.Value,
            Amount: amount is null ? null : RecallReferenceReader.Amount(amount),
            RequestedExecutionDate: RequestedExecutionDate.From(Reader.Child(reference, "ReqdExctnDt")),
            ServiceLevelCode: Reader.Code(Reader.Child(type, "SvcLvl")),
            LocalInstrumentCode: Reader.Code(Reader.Child(type, "LclInstrm")),
            CategoryPurposeCode: Reader.Code(Reader.Child(type, "CtgyPurp")),
            RemittanceUnstructured: Reader.Unstructured(Reader.Child(reference, "RmtInf")),
            DebtorAgent: Reader.Agent(Reader.Child(reference, "DbtrAgt"), "debtor"),
            CreditorAgent: Reader.Child(reference, "CdtrAgt") is { } creditorAgent ? Reader.Agent(creditorAgent, "creditor") : null,
            Creditor: creditor is null
                ? null
                : new IncomingCancelledCreditor(Reader.Value(creditor, "Nm"), Reader.Identification(creditor).Identifier, Reader.Address(Reader.Child(creditor, "PstlAdr"))),
            CreditorAccount: Reader.Account(Reader.Child(reference, "CdtrAcct")));
    }

    private static string? AssignmentBic(XElement assignment, string name) =>
        Reader.Value(Reader.Child(Reader.Child(Reader.Child(assignment, name), "Agt"), "FinInstnId"), "BICFI");

    private static IncomingTransferReadResult Hold(string reason) => new IncomingTransferReadResult.Hold(reason);
}
