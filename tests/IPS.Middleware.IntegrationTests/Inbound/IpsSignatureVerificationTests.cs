using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

// Decision 012d: an IPS certificate in configuration turns verification on; without one, every IPS signature is accepted
// and only the content decides. Annex C 2.6: IPS names its certificate by issuer and serial instead of embedding it.
// The messages are signed by our own signer and their KeyInfo rewritten, so no Java signer is needed.
public sealed class IpsSignatureVerificationTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly X509Certificate2 ips = Certificate("CN=IPS Signer, O=NBG, C=GE", Now.AddDays(-1), Now.AddDays(30));
    private readonly X509Certificate2 other = Certificate("CN=Other Signer, O=NBG, C=GE", Now.AddDays(-1), Now.AddDays(30));

    public void Dispose()
    {
        ips.Dispose();
        other.Dispose();
    }

    [Fact]
    public void Without_a_configured_ips_certificate_any_signature_is_accepted_and_only_content_decides()
    {
        var none = new IpsSignatureTrust([], TimeProvider.System);
        Assert.False(none.Verifies);

        var signed = Sign(ips);
        Assert.IsType<IncomingPacs008ReadResult.Ready>(Read(signed, none));
        var changed = Assert.IsType<IncomingPacs008ReadResult.Ready>(Read(signed.Replace("12.50", "13.50", StringComparison.Ordinal), none));
        Assert.Equal(13.50m, changed.Payment.Payment.Amount);
        Assert.IsType<IncomingPacs008ReadResult.Ready>(Read(Sign(other), none));
        Assert.IsType<IncomingPacs008ReadResult.Hold>(Read("<broken", none));
        Assert.IsType<IncomingPacs008ReadResult.Hold>(Read(signed.Replace("pacs.008.001.12", "pacs.008.001.11", StringComparison.Ordinal), none));
        Assert.IsType<IncomingPacs008ReadResult.Hold>(Read(IncomingPacs008Fixture.Xml, none));
    }

    [Theory]
    [InlineData("issuer-serial")]
    [InlineData("issuer-serial-rfc2253")]
    [InlineData("no-key-info")]
    [InlineData("embedded")]
    public void A_configured_ips_certificate_verifies_the_signature_however_ips_names_it(string naming)
    {
        var trust = new IpsSignatureTrust([ips], TimeProvider.System);
        Assert.True(trust.Verifies);
        var signed = Name(Sign(ips), ips, naming);

        Assert.IsType<IncomingPacs008ReadResult.Ready>(Read(signed, trust));
        Assert.Equal("Untrusted message signature.", Held(signed.Replace("12.50", "13.50", StringComparison.Ordinal), trust));
    }

    [Theory]
    [InlineData("issuer-serial")]
    [InlineData("no-key-info")]
    [InlineData("embedded")]
    public void A_signature_by_another_certificate_is_untrusted(string naming) =>
        Assert.Equal("Untrusted message signature.", Held(Name(Sign(other), other, naming), new IpsSignatureTrust([ips], TimeProvider.System)));

    [Fact]
    public void Issuer_and_serial_must_both_name_the_configured_certificate()
    {
        var trust = new IpsSignatureTrust([ips], TimeProvider.System);
        var signed = Name(Sign(ips), ips, "issuer-serial");
        var serial = Serial(ips).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("Untrusted message signature.", Held(signed.Replace(">" + serial + "<", ">" + (Serial(ips) + 1) + "<", StringComparison.Ordinal), trust));
        Assert.Equal("Untrusted message signature.", Held(signed.Replace("CN=IPS Signer", "CN=Someone Else", StringComparison.Ordinal), trust));
    }

    [Fact]
    public void A_configured_certificate_outside_its_validity_is_reported_as_such()
    {
        using var expired = Certificate("CN=Expired IPS, O=NBG, C=GE", Now.AddDays(-30), Now.AddDays(-1));
        var signed = Name(Sign(expired, Now.AddDays(-10)), expired, "issuer-serial");
        Assert.StartsWith("IPS certificate outside its validity period", Held(signed, new IpsSignatureTrust([expired], TimeProvider.System)), StringComparison.Ordinal);
    }

    private static IncomingPacs008ReadResult Read(string xml, IpsSignatureTrust trust) => new IncomingPacs008Reader().Read(xml, trust, Now);

    private static string Held(string xml, IpsSignatureTrust trust) => Assert.IsType<IncomingPacs008ReadResult.Hold>(Read(xml, trust)).Reason;

    private static string Sign(X509Certificate2 certificate, DateTimeOffset? at = null) =>
        new Pacs008MessageSigner(new Pacs008SigningPolicy(false, false), new FixedTime(at ?? Now))
            .Prepare(IncomingPacs008Fixture.Xml.Replace("<Sgntr/>", "", StringComparison.Ordinal), certificate).Xml;

    // KeyInfo is outside SignedInfo, so rewriting it keeps the signature valid.
    private static string Name(string signed, X509Certificate2 certificate, string naming)
    {
        if (naming == "embedded")
        {
            return signed;
        }

        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(signed);
        var ds = SignedXmlNamespace;
        var keyInfo = (XmlElement)document.GetElementsByTagName("KeyInfo", ds)[0]!;
        if (naming == "no-key-info")
        {
            keyInfo.ParentNode!.RemoveChild(keyInfo);
            return document.OuterXml;
        }

        var issuer = naming == "issuer-serial-rfc2253" ? certificate.Issuer.Replace(", ", ",", StringComparison.Ordinal) : certificate.Issuer;
        keyInfo.InnerXml =
            $"<ds:X509Data xmlns:ds=\"{ds}\"><ds:X509IssuerSerial><ds:X509IssuerName>{issuer}</ds:X509IssuerName>" +
            $"<ds:X509SerialNumber>{Serial(certificate)}</ds:X509SerialNumber></ds:X509IssuerSerial></ds:X509Data>";
        return document.OuterXml;
    }

    private const string SignedXmlNamespace = "http://www.w3.org/2000/09/xmldsig#";

    private static BigInteger Serial(X509Certificate2 certificate) => new(certificate.SerialNumberBytes.Span, isUnsigned: true, isBigEndian: true);

    private static X509Certificate2 Certificate(string subject, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        using var created = request.CreateSelfSigned(notBefore, notAfter);
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
