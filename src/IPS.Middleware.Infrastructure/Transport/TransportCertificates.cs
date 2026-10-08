using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Transport;

// Loads and validates every configured certificate once at startup and owns their lifetime.
internal sealed class TransportCertificates : ISigningCertificateSource, IDisposable
{
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    private readonly List<X509Certificate2> _owned = [];
    private readonly TimeProvider _time;
    private readonly X509Certificate2? _signing;
    private readonly X509Certificate2? _ipsClient;
    private readonly X509Certificate2? _cbsClient;
    private readonly X509Certificate2[] _ipsServerTrust = [];
    private readonly X509Certificate2[] _cbsServerTrust = [];

    public TransportCertificates(
        CertificateSettings? signingSource,
        HttpEndpointSettings ips,
        HttpEndpointSettings cbs,
        CertificateSettings[] signatureSources,
        Pacs008SigningPolicy signingPolicy,
        TimeProvider time,
        bool signingOptional = false)
    {
        _time = time;
        try
        {
            _signing = LoadSigningCertificate(signingSource, signingPolicy, signingOptional);
            _ipsClient = LoadClientCertificate(ips.ClientCertificate);
            _cbsClient = LoadClientCertificate(cbs.ClientCertificate);
            _ipsServerTrust = LoadTrust(ips.ServerTrust);
            _cbsServerTrust = LoadTrust(cbs.ServerTrust);
            IpsSignatureTrust = Array.AsReadOnly(LoadSignatureTrust(signatureSources));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public IReadOnlyCollection<X509Certificate2> IpsSignatureTrust { get; } = [];

    public ValueTask<X509Certificate2?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_signing);
    }

    internal IReadOnlyList<CertificateExpiry> Expiries(string source) => _owned
        .Select(certificate => new CertificateExpiry(
            source, certificate.Subject, certificate.NotAfter.ToUniversalTime(), certificate.NotBefore.ToUniversalTime()))
        .ToArray();

    public void Dispose()
    {
        foreach (var certificate in _owned)
        {
            certificate.Dispose();
        }

        _owned.Clear();
    }

    internal SslClientAuthenticationOptions Tls(bool ips, bool revocation)
    {
        var client = ips ? _ipsClient : _cbsClient;
        var trust = ips ? _ipsServerTrust : _cbsServerTrust;
        var options = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = revocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
            ClientCertificates = client is null ? null : new X509CertificateCollection { client }
        };
        if (trust.Length > 0)
        {
            options.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                ValidateServerCertificate(certificate, errors, trust, options.CertificateRevocationCheckMode);
        }

        return options;
    }

    private X509Certificate2? LoadSigningCertificate(CertificateSettings? source, Pacs008SigningPolicy signingPolicy, bool signingOptional)
    {
        var signing = Load(source, privateKeyRequired: true);
        if (signing is null)
        {
            // The Proxy Solution signs only when a certificate is configured, as in the source; the IPS messages never do.
            return signingOptional || signingPolicy.AllowUnsignedWithoutCertificate
                ? null
                : throw new InvalidOperationException("A signing certificate or explicit Development unsigned policy is required.");
        }

        CertificateSettings.RequireDigitalSignature(signing);
        using var key = signing.GetECDsaPrivateKey();
        if (key is null)
        {
            throw new InvalidOperationException("XML signing requires an ECDSA private key.");
        }

        return signing;
    }

    private X509Certificate2? LoadClientCertificate(CertificateSettings? source)
    {
        var client = Load(source, privateKeyRequired: true, forTls: true);
        if (client is not null)
        {
            CertificateSettings.RequireClientAuthentication(client);
        }

        return client;
    }

    private X509Certificate2[] LoadSignatureTrust(CertificateSettings[] sources)
    {
        var trust = sources
            .Select(source => Own(source.LoadSignatureTrust(_time.GetUtcNow())))
            .ToArray();
        foreach (var certificate in trust)
        {
            CertificateSettings.RequireDigitalSignature(certificate);
            using var key = certificate.GetECDsaPublicKey();
            if (key is null)
            {
                throw new InvalidOperationException("IPS signature trust requires ECDSA public keys.");
            }
        }

        return trust;
    }

    private X509Certificate2[] LoadTrust(CertificateSettings[] sources) =>
        sources.Select(source => Load(source, privateKeyRequired: false)!).ToArray();

    private X509Certificate2? Load(CertificateSettings? source, bool privateKeyRequired, bool forTls = false)
    {
        if (source is null)
        {
            return null;
        }

        return Own(source.Load(privateKeyRequired, _time.GetUtcNow(), forTls));
    }

    private X509Certificate2 Own(X509Certificate2 certificate)
    {
        _owned.Add(certificate);
        return certificate;
    }

    // Custom chain trust never bypasses the platform's hostname or missing-certificate checks.
    private static bool ValidateServerCertificate(
        X509Certificate? certificate,
        SslPolicyErrors errors,
        X509Certificate2[] trust,
        X509RevocationMode revocation)
    {
        if (certificate is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
        {
            return false;
        }

        using var leaf = new X509Certificate2(certificate);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = revocation;
        chain.ChainPolicy.ApplicationPolicy.Add(new(ServerAuthenticationOid));
        foreach (var authority in trust)
        {
            var selfSigned = authority.SubjectName.RawData.AsSpan().SequenceEqual(authority.IssuerName.RawData);
            if (selfSigned)
            {
                chain.ChainPolicy.CustomTrustStore.Add(authority);
            }
            else
            {
                chain.ChainPolicy.ExtraStore.Add(authority);
            }
        }

        return chain.Build(leaf);
    }
}
