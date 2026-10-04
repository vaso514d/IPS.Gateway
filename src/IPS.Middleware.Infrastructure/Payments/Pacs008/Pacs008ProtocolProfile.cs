using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008;

public sealed class Pacs008ProtocolProfile
{
    public Pacs008ProtocolProfile(string ipsBic, string serviceLevelCode = InstantServiceLevel,
        RemittanceDeliveryMethod remittanceMethod = RemittanceDeliveryMethod.Uri)
    {
        if (ipsBic is null || !Regex.IsMatch(ipsBic, @"^[A-Z0-9]{4}[A-Z]{2}[A-Z0-9]{2}([A-Z0-9]{3})?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new ArgumentException("A valid IPS BIC is required.", nameof(ipsBic));
        if (serviceLevelCode is not { Length: >= 1 and <= 4 })
            throw new ArgumentException("Service level must contain between one and four characters.", nameof(serviceLevelCode));
        if (!Enum.IsDefined(remittanceMethod))
            throw new ArgumentOutOfRangeException(nameof(remittanceMethod));
        IpsBic = ipsBic;
        ServiceLevelCode = serviceLevelCode;
        RemittanceMethod = remittanceMethod;
    }

    public string IpsBic { get; }
    public string ServiceLevelCode { get; }
    public RemittanceDeliveryMethod RemittanceMethod { get; }
    public const string InstantServiceLevel = "INST";
    internal const string MessageDefinition = "pacs.008.001.12";
    internal const string ClearingSystem = "IPS";
    internal const string LocalInstrument = "INST";
    internal const string IndirectClearingSystem = "GE";
    internal const string BillIdentificationScheme = "BILL";
    internal static readonly TimeSpan SettlementOffset = TimeSpan.FromHours(4);
}

public enum RemittanceDeliveryMethod
{
    [XmlEnum("FAXI")] Fax,
    [XmlEnum("EDIC")] ElectronicDataInterchange,
    [XmlEnum("URID")] Uri,
    [XmlEnum("EMAL")] Email,
    [XmlEnum("POST")] Post,
    [XmlEnum("SMSM")] Sms
}
