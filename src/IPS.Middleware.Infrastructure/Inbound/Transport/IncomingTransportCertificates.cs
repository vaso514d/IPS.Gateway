using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Inbound.Transport;

public sealed class IncomingTransportCertificates : ISigningCertificateSource, IDisposable
{
    private readonly List<X509Certificate2> owned = [];
    private readonly X509Certificate2? signing;
    private readonly X509Certificate2? ipsClient;
    private readonly X509Certificate2? cbsClient;
    private readonly X509Certificate2[] ipsServerTrust;
    private readonly X509Certificate2[] cbsServerTrust;
    public IReadOnlyCollection<X509Certificate2> IpsSignatureTrust { get; }

    public IncomingTransportCertificates(IncomingTransportSettings settings, Pacs008SigningPolicy signingPolicy, TimeProvider time)
    {
        try
        {
            signing = Load(settings.SigningCertificate, true);
            if (signing is null && !signingPolicy.AllowUnsignedWithoutCertificate)
                throw new InvalidOperationException("A signing certificate or explicit Development unsigned policy is required.");
            if (signing is not null)
            {
                CertificateSettings.RequireDigitalSignature(signing);
                using var key = signing.GetECDsaPrivateKey();
                if (key is null) throw new InvalidOperationException("XML signing requires an ECDSA private key.");
            }
            ipsClient = Load(settings.Ips.ClientCertificate, true, true);
            cbsClient = Load(settings.Cbs.ClientCertificate, true, true);
            foreach (var client in new[] { ipsClient, cbsClient }.OfType<X509Certificate2>()) CertificateSettings.RequireClientAuthentication(client);
            ipsServerTrust = LoadTrust(settings.Ips.ServerTrust);
            cbsServerTrust = LoadTrust(settings.Cbs.ServerTrust);
            var signatureTrust = LoadTrust(settings.IpsSignatureTrust);
            foreach (var certificate in signatureTrust)
            {
                CertificateSettings.RequireDigitalSignature(certificate);
                using var key = certificate.GetECDsaPublicKey();
                if (key is null) throw new InvalidOperationException("IPS signature trust requires ECDSA public keys.");
            }
            IpsSignatureTrust = Array.AsReadOnly(signatureTrust);
        }
        catch { Dispose(); throw; }

        X509Certificate2? Load(CertificateSettings? source, bool key, bool tls = false)
        {
            if (source is null) return null;
            var certificate = source.Load(key, time.GetUtcNow(), tls);
            owned.Add(certificate);
            return certificate;
        }
        X509Certificate2[] LoadTrust(CertificateSettings[] sources) => sources.Select(source => Load(source, false)!).ToArray();
    }

    public ValueTask<X509Certificate2?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(signing);
    }

    internal SslClientAuthenticationOptions Tls(bool ips, bool revocation)
    {
        var client = ips ? ipsClient : cbsClient;
        var trust = ips ? ipsServerTrust : cbsServerTrust;
        var options = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = revocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
            ClientCertificates = client is null ? null : new X509CertificateCollection { client }
        };
        if (trust.Length > 0)
        {
            options.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
            {
                // Custom chain trust never bypasses the platform's hostname or missing-certificate checks.
                if (certificate is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0) return false;
                using var leaf = new X509Certificate2(certificate);
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.RevocationMode = options.CertificateRevocationCheckMode;
                chain.ChainPolicy.ApplicationPolicy.Add(new("1.3.6.1.5.5.7.3.1"));
                foreach (var ca in trust)
                {
                    if (ca.SubjectName.RawData.AsSpan().SequenceEqual(ca.IssuerName.RawData)) chain.ChainPolicy.CustomTrustStore.Add(ca);
                    else chain.ChainPolicy.ExtraStore.Add(ca);
                }
                return chain.Build(leaf);
            };
        }
        return options;
    }

    public void Dispose()
    {
        foreach (var certificate in owned) certificate.Dispose();
        owned.Clear();
    }
}
