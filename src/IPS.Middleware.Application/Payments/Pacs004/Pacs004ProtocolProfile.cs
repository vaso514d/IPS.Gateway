using System.Text.RegularExpressions;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Payments.Pacs004;

// Configurable XML mapping settings; intake snapshots them with each accepted payment.
public sealed record Pacs004ProtocolProfile
{
    public const string InstantServiceLevel = "INST";

    public Pacs004ProtocolProfile(string ipsBic, string serviceLevelCode = InstantServiceLevel)
    {
        if (ipsBic is null || !Regex.IsMatch(ipsBic, Pacs008Text.Bic, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            throw new ArgumentException("A valid IPS BIC is required.", nameof(ipsBic));
        }

        if (serviceLevelCode is not { Length: >= 1 and <= 4 })
        {
            throw new ArgumentException("Service level must contain between one and four characters.", nameof(serviceLevelCode));
        }

        IpsBic = ipsBic;
        ServiceLevelCode = serviceLevelCode;
    }

    public string IpsBic { get; }
    public string ServiceLevelCode { get; }
}
