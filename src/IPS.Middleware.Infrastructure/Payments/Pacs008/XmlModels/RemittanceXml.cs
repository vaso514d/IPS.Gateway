using System.Xml.Serialization;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008.XmlModels;

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class RegulatoryXml
{
    [XmlElement("Dtls", Order = 0)] public required RegulatoryDetailsXml Details { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class RegulatoryDetailsXml
{
    [XmlElement("Cd", Order = 0)] public string? ChannelCode { get; set; }
    [XmlElement("Inf", Order = 1)] public string[] Geolocation { get; set; } = [];
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class RelatedRemittanceXml
{
    [XmlElement("RmtId", Order = 0)] public required string Id { get; set; }
    [XmlElement("RmtLctnDtls", Order = 1)] public RemittanceLocationXml? Location { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class RemittanceLocationXml
{
    [XmlElement("Mtd", Order = 0)] public RemittanceDeliveryMethod Method { get; set; }
    [XmlElement("ElctrncAdr", Order = 1)] public required string ElectronicAddress { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class RemittanceXml
{
    [XmlElement("Ustrd", Order = 0)] public string[] Unstructured { get; set; } = [];
    [XmlElement("Strd", Order = 1)] public StructuredRemittanceXml[] Structured { get; set; } = [];
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class StructuredRemittanceXml
{
    [XmlElement("CdtrRefInf", Order = 0)] public required CreditorReferenceXml CreditorReference { get; set; }
    [XmlElement("AddtlRmtInf", Order = 1)] public string[] AdditionalInformation { get; set; } = [];
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class CreditorReferenceXml
{
    [XmlElement("Tp", Order = 0)] public required ReferenceTypeXml Type { get; set; }
    [XmlElement("Ref", Order = 1)] public required string Reference { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class ReferenceTypeXml
{
    [XmlElement("CdOrPrtry", Order = 0)] public required ProprietaryCodeXml Code { get; set; }
    [XmlElement("Issr", Order = 1)] public string? Issuer { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class ProprietaryCodeXml
{
    [XmlElement("Prtry", Order = 0)] public required string Value { get; set; }
}
