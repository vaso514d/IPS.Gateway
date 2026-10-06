using System.Globalization;
using System.Xml.Linq;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs009;
using IPS.Middleware.Infrastructure.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Payments.Pacs009;

// Builds the supported pacs.009 profile (IPS v1): financial institutions identified by BICFI only, accounts as IBAN only,
// no UETR, instruction priority or clearing-system member id. Child order follows the XSD sequence; null children are omitted.
internal static class Pacs009Message
{
    private static readonly XNamespace H = Pacs008Xml.HeaderNamespace;
    private static readonly XNamespace P = Pacs009Xml.DocumentNamespace;
    private const int RemittanceLineLength = 140;
    private const string ClearingSystem = "IPS";
    private const string LocalInstrument = "INST";

    internal static XElement Build(ValidatedPacs009 payment, PaymentMessageContext context, IpsMessageProfile profile) =>
        new("Message",
            new XAttribute(XNamespace.Xmlns + "head", H.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "pacs", P.NamespaceName),
            Header(payment, context, profile),
            new XElement(P + "Document",
                new XElement(P + "FICdtTrf",
                    GroupHeader(payment, context, profile),
                    Transaction(payment, context))));

    private static XElement Header(ValidatedPacs009 payment, PaymentMessageContext context, IpsMessageProfile profile) =>
        new(H + "AppHdr",
            HeaderParty("Fr", payment.ParticipantBic),
            HeaderParty("To", profile.IpsBic),
            new XElement(H + "BizMsgIdr", context.MessageId),
            new XElement(H + "MsgDefIdr", PaymentMessageTypes.Pacs009Definition),
            new XElement(H + "CreDt", Timestamp(context.EnvelopeCreatedAtUtc)));

    private static XElement HeaderParty(string name, string bic) =>
        new(H + name, new XElement(H + "FIId", new XElement(H + "FinInstnId", new XElement(H + "BICFI", bic))));

    private static XElement GroupHeader(ValidatedPacs009 payment, PaymentMessageContext context, IpsMessageProfile profile) =>
        new(P + "GrpHdr",
            new XElement(P + "MsgId", context.MessageId),
            new XElement(P + "CreDtTm", Timestamp(context.EnvelopeCreatedAtUtc)),
            new XElement(P + "NbOfTxs", 1),
            Amount("TtlIntrBkSttlmAmt", payment),
            new XElement(P + "IntrBkSttlmDt", Date(payment.ValueDate)),
            new XElement(P + "SttlmInf",
                new XElement(P + "SttlmMtd", "CLRG"),
                Code("ClrSys", ClearingSystem)),
            new XElement(P + "PmtTpInf",
                Code("SvcLvl", profile.ServiceLevelCode),
                Code("LclInstrm", LocalInstrument),
                Choice("CtgyPurp", payment.CategoryPurpose, payment.CategoryPurposeIsProprietary)),
            new XElement(P + "InstgAgt", FinancialInstitution(payment.ParticipantBic)));

    private static XElement Transaction(ValidatedPacs009 payment, PaymentMessageContext context) =>
        new(P + "CdtTrfTxInf",
            new XElement(P + "PmtId",
                Optional("InstrId", payment.InstructionId),
                new XElement(P + "EndToEndId", payment.EndToEndId),
                new XElement(P + "TxId", context.TransactionId)),
            Amount("IntrBkSttlmAmt", payment),
            new XElement(P + "IntrBkSttlmDt", Date(payment.ValueDate)),
            new XElement(P + "Dbtr", FinancialInstitution(payment.DebtorAgentBic)),
            Account("DbtrAcct", payment.DebtorAccount),
            new XElement(P + "DbtrAgt", FinancialInstitution(payment.DebtorAgentBic)),
            new XElement(P + "CdtrAgt", FinancialInstitution(payment.CreditorAgentBic)),
            new XElement(P + "Cdtr", FinancialInstitution(payment.CreditorAgentBic)),
            Account("CdtrAcct", payment.CreditorAccount),
            Choice("Purp", payment.Purpose, payment.PurposeIsProprietary),
            Remittance(payment.AdditionalPurpose));

    private static XElement FinancialInstitution(string bic) =>
        new(P + "FinInstnId", new XElement(P + "BICFI", bic));

    private static XElement? Account(string name, string? iban) =>
        iban is null ? null : new(P + name, new XElement(P + "Id", new XElement(P + "IBAN", iban)));

    private static XElement Amount(string name, ValidatedPacs009 payment) =>
        new(P + name, new XAttribute("Ccy", payment.Currency), payment.Amount.ToString("0.#####", CultureInfo.InvariantCulture));

    private static XElement Code(string name, string code) => new(P + name, new XElement(P + "Cd", code));

    private static XElement? Choice(string name, string? value, bool proprietary) =>
        value is null ? null : new(P + name, new XElement(P + (proprietary ? "Prtry" : "Cd"), value));

    // new XElement(name, null) would emit an empty element; absent values must be omitted.
    private static XElement? Optional(string name, string? value) => value is null ? null : new(P + name, value);

    // RmtInf/Ustrd is unbounded Max140Text, so the text is split into 140-character elements.
    private static XElement? Remittance(string? text) =>
        text is null ? null : new(P + "RmtInf", Chunks(text, RemittanceLineLength).Select(line => new XElement(P + "Ustrd", line)));

    private static IEnumerable<string> Chunks(string text, int length)
    {
        for (var offset = 0; offset < text.Length; offset += length)
        {
            yield return text.Substring(offset, Math.Min(length, text.Length - offset));
        }
    }

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Timestamp(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
