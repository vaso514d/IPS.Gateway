using IPS.Middleware.Infrastructure.Transport;

namespace IPS.Middleware.Infrastructure.Proxy;

public sealed class ProxySettings
{
    public bool Enabled { get; init; }
    public string ParticipantBic { get; init; } = "";
    public string ProxyBic { get; init; } = "";
    public string ProtocolVersion { get; init; } = "1";
    public HttpEndpointSettings Endpoint { get; init; } = new() { RequestTimeout = TimeSpan.FromSeconds(15) };
    // Optional: without a certificate the message is sent unsigned, as in the source.
    public CertificateSettings? SigningCertificate { get; init; }

    public void Validate(bool development)
    {
        if (!Enabled)
        {
            return;
        }

        RequireBic(ParticipantBic, nameof(ParticipantBic));
        RequireBic(ProxyBic, nameof(ProxyBic));
        if (string.IsNullOrWhiteSpace(ProtocolVersion) || ProtocolVersion.Any(c => c < 33 || c > 126))
        {
            throw new InvalidOperationException("Proxy protocol version must be a nonempty ASCII header value.");
        }

        Endpoint.Validate(development);
        if (!development && Endpoint.ClientCertificate is null)
        {
            throw new InvalidOperationException("Proxy mutual TLS requires a client certificate.");
        }
    }

    private static void RequireBic(string bic, string name)
    {
        if (bic.Length is not (8 or 11) || bic.Any(c => !char.IsAsciiLetterOrDigit(c)))
        {
            throw new InvalidOperationException($"Proxy requires an 8 or 11 character {name}.");
        }
    }
}
