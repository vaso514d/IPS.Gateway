using System.Text.RegularExpressions;

namespace IPS.Middleware.Application.Payments.Pacs008;

// Configurable XML mapping settings; intake snapshots them with each accepted payment.
public sealed class Pacs008ProtocolProfile
{
    // PmtTpInf/SvcLvl/Cd. IPS UAT rejects INST here (1026); SEPA is the SEPA Instant service level Annex D 3 follows.
    public const string DefaultServiceLevel = "SEPA";

    // PmtTpInf/LclInstrm/Cd of an instant payment (Annex D 3.2.1, 4d).
    public const string InstantLocalInstrument = "INST";

    public Pacs008ProtocolProfile(
        string ipsBic,
        string serviceLevelCode = DefaultServiceLevel,
        RemittanceDeliveryMethod remittanceMethod = RemittanceDeliveryMethod.Uri)
    {
        if (ipsBic is null || !Regex.IsMatch(ipsBic, Pacs008Text.Bic, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            throw new ArgumentException("A valid IPS BIC is required.", nameof(ipsBic));
        }

        if (serviceLevelCode is not { Length: >= 1 and <= 4 })
        {
            throw new ArgumentException("Service level must contain between one and four characters.", nameof(serviceLevelCode));
        }

        if (!Enum.IsDefined(remittanceMethod))
        {
            throw new ArgumentOutOfRangeException(nameof(remittanceMethod));
        }

        IpsBic = ipsBic;
        ServiceLevelCode = serviceLevelCode;
        RemittanceMethod = remittanceMethod;
    }

    public string IpsBic { get; }
    public string ServiceLevelCode { get; }
    public RemittanceDeliveryMethod RemittanceMethod { get; }
}

public enum RemittanceDeliveryMethod
{
    Fax,
    ElectronicDataInterchange,
    Uri,
    Email,
    Post,
    Sms
}
