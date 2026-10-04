using System.Xml.Serialization;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008.XmlModels;

[XmlRoot("Message", Namespace = "")]
public sealed class MessageXml
{
    [XmlElement("AppHdr", Namespace = Pacs008Xml.HeaderNamespace, Order = 0)]
    public required HeaderXml Header { get; set; }
    [XmlElement("Document", Namespace = Pacs008Xml.DocumentNamespace, Order = 1)]
    public required DocumentXml Document { get; set; }
}

[XmlType(Namespace = Pacs008Xml.HeaderNamespace)]
public sealed class HeaderXml
{
    [XmlElement("Fr", Order = 0)] public required HeaderPartyXml From { get; set; }
    [XmlElement("To", Order = 1)] public required HeaderPartyXml To { get; set; }
    [XmlElement("BizMsgIdr", Order = 2)] public required string MessageId { get; set; }
    [XmlElement("MsgDefIdr", Order = 3)] public required string MessageDefinition { get; set; }
    [XmlElement("CreDt", Order = 4)] public required string CreatedAt { get; set; }
}

[XmlType(Namespace = Pacs008Xml.HeaderNamespace)]
public sealed class HeaderPartyXml
{
    [XmlElement("FIId", Order = 0)] public required HeaderInstitutionXml Institution { get; set; }
}

[XmlType(Namespace = Pacs008Xml.HeaderNamespace)]
public sealed class HeaderInstitutionXml
{
    [XmlElement("FinInstnId", Order = 0)] public required HeaderInstitutionIdXml Identification { get; set; }
}

[XmlType(Namespace = Pacs008Xml.HeaderNamespace)]
public sealed class HeaderInstitutionIdXml
{
    [XmlElement("BICFI", Order = 0)] public required string Bic { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class DocumentXml
{
    [XmlElement("FIToFICstmrCdtTrf", Order = 0)] public required CreditTransferXml CreditTransfer { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class CreditTransferXml
{
    [XmlElement("GrpHdr", Order = 0)] public required GroupHeaderXml Group { get; set; }
    [XmlElement("CdtTrfTxInf", Order = 1)] public required TransactionXml Transaction { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class GroupHeaderXml
{
    [XmlElement("MsgId", Order = 0)] public required string MessageId { get; set; }
    [XmlElement("CreDtTm", Order = 1)] public required string CreatedAt { get; set; }
    [XmlElement("NbOfTxs", Order = 2)] public int TransactionCount { get; set; }
    [XmlElement("TtlIntrBkSttlmAmt", Order = 3)] public required AmountXml TotalAmount { get; set; }
    [XmlElement("IntrBkSttlmDt", Order = 4)] public required string SettlementDate { get; set; }
    [XmlElement("SttlmInf", Order = 5)] public required SettlementXml Settlement { get; set; }
    [XmlElement("PmtTpInf", Order = 6)] public required PaymentTypeXml PaymentType { get; set; }
    [XmlElement("InstgAgt", Order = 7)] public required AgentXml InstructingAgent { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class SettlementXml
{
    [XmlElement("SttlmMtd", Order = 0)] public SettlementMethod Method { get; set; }
    [XmlElement("ClrSys", Order = 1)] public required CodeXml ClearingSystem { get; set; }
}

public enum SettlementMethod { [XmlEnum("CLRG")] Clearing }
public enum InstructionPriority { [XmlEnum("NORM")] Normal, [XmlEnum("HIGH")] High }
public enum ChargeBearer { [XmlEnum("SLEV")] ServiceLevel }

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class PaymentTypeXml
{
    [XmlElement("InstrPrty", Order = 0)] public InstructionPriority Priority { get; set; }
    [XmlElement("SvcLvl", Order = 1)] public required CodeXml ServiceLevel { get; set; }
    [XmlElement("LclInstrm", Order = 2)] public required CodeXml LocalInstrument { get; set; }
    [XmlElement("CtgyPurp", Order = 3)] public CodeXml? CategoryPurpose { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class CodeXml
{
    [XmlElement("Cd", Order = 0)] public required string Code { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class AmountXml
{
    [XmlAttribute("Ccy")] public required string Currency { get; set; }
    [XmlText] public required string Value { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class TransactionXml
{
    [XmlElement("PmtId", Order = 0)] public required PaymentIdXml Identification { get; set; }
    [XmlElement("IntrBkSttlmAmt", Order = 1)] public required AmountXml Amount { get; set; }
    [XmlElement("AccptncDtTm", Order = 2)] public required string AcceptedAt { get; set; }
    [XmlElement("ChrgBr", Order = 3)] public ChargeBearer Charges { get; set; }
    [XmlElement("UltmtDbtr", Order = 4)] public PartyXml? UltimateDebtor { get; set; }
    [XmlElement("Dbtr", Order = 5)] public required PartyXml Debtor { get; set; }
    [XmlElement("DbtrAcct", Order = 6)] public required AccountXml DebtorAccount { get; set; }
    [XmlElement("DbtrAgt", Order = 7)] public required AgentXml DebtorAgent { get; set; }
    [XmlElement("CdtrAgt", Order = 8)] public required AgentXml CreditorAgent { get; set; }
    [XmlElement("Cdtr", Order = 9)] public required PartyXml Creditor { get; set; }
    [XmlElement("CdtrAcct", Order = 10)] public required AccountXml CreditorAccount { get; set; }
    [XmlElement("UltmtCdtr", Order = 11)] public PartyXml? UltimateCreditor { get; set; }
    [XmlElement("RgltryRptg", Order = 12)] public RegulatoryXml? Regulatory { get; set; }
    [XmlElement("RltdRmtInf", Order = 13)] public RelatedRemittanceXml[] RelatedRemittance { get; set; } = [];
    [XmlElement("RmtInf", Order = 14)] public RemittanceXml? Remittance { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class PaymentIdXml
{
    [XmlElement("InstrId", Order = 0)] public required string InstructionId { get; set; }
    [XmlElement("EndToEndId", Order = 1)] public required string EndToEndId { get; set; }
    [XmlElement("TxId", Order = 2)] public required string TransactionId { get; set; }
}
