using System.Xml.Serialization;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008.XmlModels;

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class PartyXml
{
    [XmlElement("Nm", Order = 0)] public required string Name { get; set; }
    [XmlElement("PstlAdr", Order = 1)] public AddressXml? Address { get; set; }
    [XmlElement("Id", Order = 2)] public PartyIdXml? Identification { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class PartyIdXml
{
    [XmlElement("OrgId", Order = 0)] public PartyIdentifiersXml? Organisation { get; set; }
    [XmlElement("PrvtId", Order = 1)] public PartyIdentifiersXml? Individual { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class PartyIdentifiersXml
{
    [XmlElement("Othr", Order = 0)] public IdentifierXml[] Other { get; set; } = [];
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class IdentifierXml
{
    [XmlElement("Id", Order = 0)] public required string Value { get; set; }
    [XmlElement("SchmeNm", Order = 1)] public CodeXml? Scheme { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class AddressXml
{
    [XmlElement("StrtNm", Order = 0)] public string? Street { get; set; }
    [XmlElement("BldgNb", Order = 1)] public string? Building { get; set; }
    [XmlElement("PstCd", Order = 2)] public string? PostCode { get; set; }
    [XmlElement("TwnNm", Order = 3)] public string? Town { get; set; }
    [XmlElement("CtrySubDvsn", Order = 4)] public string? Subdivision { get; set; }
    [XmlElement("Ctry", Order = 5)] public string? Country { get; set; }
    [XmlElement("AdrLine", Order = 6)] public string[] Lines { get; set; } = [];
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class AccountXml
{
    [XmlElement("Id", Order = 0)] public required AccountIdXml Identification { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class AccountIdXml
{
    [XmlElement("IBAN", Order = 0)] public string? Iban { get; set; }
    [XmlElement("Othr", Order = 1)] public IdentifierXml? Other { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class AgentXml
{
    [XmlElement("FinInstnId", Order = 0)] public required InstitutionIdXml Identification { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class InstitutionIdXml
{
    [XmlElement("BICFI", Order = 0)] public required string Bic { get; set; }
    [XmlElement("ClrSysMmbId", Order = 1)] public ClearingMemberXml? ClearingMember { get; set; }
}

[XmlType(Namespace = Pacs008Xml.DocumentNamespace)]
public sealed class ClearingMemberXml
{
    [XmlElement("ClrSysId", Order = 0)] public required CodeXml ClearingSystem { get; set; }
    [XmlElement("MmbId", Order = 1)] public required string MemberId { get; set; }
}
