using IPS.Middleware.Infrastructure.Transport;
using IPS.MiidleWear.Security;

namespace IPS.Middleware.Api.Authentication;

// The core system's signing certificates the gateway trusts (specification 021). None configured: requests are not
// checked. Algorithms empty: RS256, PS256 and ES256 are all accepted. Configuration binding appends to an array's
// initial items, so the defaults are not written into the arrays.
public sealed class CoreAuthenticationSettings
{
    public CertificateSettings[] TrustedCertificates { get; init; } = [];
    public string[] Algorithms { get; init; } = [];

    internal bool RequiresSignature => TrustedCertificates.Length > 0;

    internal IReadOnlyList<IpsSignatureAlgorithm> AllowedAlgorithms()
    {
        if (Algorithms.Length == 0)
        {
            return Enum.GetValues<IpsSignatureAlgorithm>();
        }

        var allowed = new List<IpsSignatureAlgorithm>();
        foreach (var name in Algorithms)
        {
            if (!IpsContentSignature.TryParse(name?.Trim(), out var algorithm))
            {
                throw new InvalidOperationException($"CoreAuthentication:Algorithms accepts RS256, PS256 and ES256, not '{name}'.");
            }

            allowed.Add(algorithm);
        }

        return allowed;
    }
}
