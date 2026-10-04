using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using IPS.Middleware.IntegrationTests.Transactions;
using Xunit;
using static IPS.Middleware.IntegrationTests.Payments.Pacs008Fixture;

namespace IPS.Middleware.IntegrationTests.Payments;

public sealed class Pacs008SigningTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly XNamespace Ds = "http://www.w3.org/2000/09/xmldsig#";
    private static readonly XNamespace Head = Pacs008Xml.HeaderNamespace;
    private static readonly XNamespace Pacs = Pacs008Xml.DocumentNamespace;

    [Theory]
    [InlineData("original")]
    [InlineData("extra-prefix")]
    [InlineData("default-header")]
    public async Task Independent_verifier_accepts_signature_with_inherited_namespace_context(string namespaceContext)
    {
        using var certificate = Certificate();
        var unsigned = Unsigned();
        if (namespaceContext != "original")
        {
            var document = XDocument.Parse(unsigned);
            if (namespaceContext == "extra-prefix")
                document.Root!.SetAttributeValue(XNamespace.Xmlns + "extra", "urn:independent:namespace");
            else
                document.Descendants(Head + "AppHdr").Single().SetAttributeValue("xmlns", Head.NamespaceName);
            unsigned = document.ToString(SaveOptions.DisableFormatting);
        }
        var result = Signer().Prepare(unsigned, certificate);
        Assert.True(result.IsSigned);
        Assert.True(await JavaSignatureVerifier.VerifyAsync(result.Xml, certificate));
        var signed = XDocument.Parse(result.Xml);
        var signature = Assert.Single(signed.Descendants(Ds + "Signature"));
        Assert.Equal("MONT", signature.Attribute("Id")!.Value);
        Assert.Equal(Head + "Sgntr", signature.Parent!.Name);
        Assert.Equal("http://www.w3.org/2006/12/xml-c14n11", signature.Descendants(Ds + "CanonicalizationMethod").Single().Attribute("Algorithm")!.Value);
        Assert.Equal("http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256", signature.Descendants(Ds + "SignatureMethod").Single().Attribute("Algorithm")!.Value);
        Assert.Equal("", signature.Descendants(Ds + "Reference").Single().Attribute("URI")!.Value);
        Assert.Equal(new[] { "http://www.w3.org/2000/09/xmldsig#enveloped-signature", "http://www.w3.org/TR/2001/REC-xml-c14n-20010315" },
            signature.Descendants(Ds + "Transform").Select(transform => transform.Attribute("Algorithm")!.Value));
        Assert.Equal("http://www.w3.org/2001/04/xmlenc#sha256", signature.Descendants(Ds + "DigestMethod").Single().Attribute("Algorithm")!.Value);
        Assert.Equal(64, Convert.FromBase64String(signature.Element(Ds + "SignatureValue")!.Value).Length);
        Assert.Equal(certificate.SubjectName.Name, signature.Descendants(Ds + "X509SubjectName").Single().Value);
        Assert.Equal(certificate.RawData, Convert.FromBase64String(signature.Descendants(Ds + "X509Certificate").Single().Value));
    }

    [Theory]
    [InlineData("header")]
    [InlineData("amount")]
    [InlineData("signature")]
    public async Task Independent_verifier_rejects_tampering(string target)
    {
        using var certificate = Certificate();
        var signed = XDocument.Parse(Signer().Prepare(Unsigned(), certificate).Xml);
        if (target == "header") signed.Descendants(Head + "BizMsgIdr").Single().Value = "tampered";
        else if (target == "amount") signed.Descendants(Pacs + "IntrBkSttlmAmt").Single().Value = "999";
        else
        {
            var value = signed.Descendants(Ds + "SignatureValue").Single();
            var bytes = Convert.FromBase64String(value.Value);
            bytes[0] ^= 1;
            value.Value = Convert.ToBase64String(bytes);
        }
        Assert.False(await JavaSignatureVerifier.VerifyAsync(signed.ToString(SaveOptions.DisableFormatting), certificate));
    }

    [Fact]
    public async Task Independent_verifier_uses_the_supplied_key_instead_of_trusting_embedded_key_info()
    {
        using var certificate = Certificate();
        using var differentCertificate = Certificate();
        var result = Signer().Prepare(Unsigned(), certificate);
        Assert.False(await JavaSignatureVerifier.VerifyAsync(result.Xml, differentCertificate));
    }

    [Fact]
    public void Missing_certificate_requires_explicit_development_permission()
    {
        var unsigned = Unsigned();
        Assert.Throws<InvalidOperationException>(() => Signer().Prepare(unsigned, null));
        Assert.Throws<InvalidOperationException>(() => new Pacs008SigningPolicy(true, false));
        Assert.Throws<InvalidOperationException>(() => new Pacs008MessageSigner(new(false, false), new FixedClock()).Prepare(unsigned, null));
        var result = Signer(allowUnsigned: true).Prepare(unsigned, null);
        Assert.False(result.IsSigned);
        Assert.Equal(unsigned, result.Xml);
    }

    [Fact]
    public void Development_permission_does_not_skip_signing_when_a_certificate_is_supplied()
    {
        using var certificate = Certificate();
        Assert.True(Signer(allowUnsigned: true).Prepare(Unsigned(), certificate).IsSigned);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("public-only")]
    [InlineData("wrong-usage")]
    [InlineData("rsa")]
    public void Unusable_certificates_fail_even_when_development_bypass_is_enabled(string kind)
    {
        using var certificate = Certificate(kind);
        Assert.Throws<InvalidOperationException>(() => Signer(allowUnsigned: true).Prepare(Unsigned(), certificate));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("base")]
    [InlineData("lang")]
    [InlineData("space")]
    public void Unsupported_inherited_xml_attributes_are_rejected(string attribute)
    {
        using var certificate = Certificate();
        var document = XDocument.Parse(Unsigned());
        document.Root!.SetAttributeValue(XNamespace.Xml + attribute, attribute == "space" ? "preserve" : "test");
        Assert.Throws<InvalidOperationException>(() => Signer().Prepare(document.ToString(SaveOptions.DisableFormatting), certificate));
    }

    [Fact]
    public void Already_signed_and_unsafe_xml_are_rejected()
    {
        using var certificate = Certificate();
        var signer = Signer();
        var signed = signer.Prepare(Unsigned(), certificate);
        Assert.Throws<InvalidOperationException>(() => signer.Prepare(signed.Xml, certificate));
        Assert.Throws<System.Xml.XmlException>(() => signer.Prepare("<!DOCTYPE Message [<!ENTITY e SYSTEM 'file:///nonexistent'>]><Message>&e;</Message>", certificate));
    }

    [Fact]
    public async Task Stored_unsigned_message_is_signed_once_and_reloaded_without_changing_artifacts()
    {
        using var certificate = Certificate();
        await using var database = await SqlTestDatabase.CreateAsync();
        Guid paymentId;
        TransactionClaim claim;
        string unsigned;
        await using (var intake = database.Session())
        {
            var payment = (await intake.Intake(Now).AcceptAsync(
                ValidatedIntakeRequest.Validate("pacs.008", "signed-preparation", System.Text.Json.JsonSerializer.Serialize(Request())).Request!, default)).Payment;
            paymentId = payment.Id;
            claim = (await intake.Processing(Now).TryStartAsync(paymentId, TimeSpan.FromSeconds(45), default))!;
            var preparation = new PaymentPreparationRepository(intake.Context);
            var identifiers = (await preparation.ReadAsync(paymentId, default))!;
            unsigned = new Pacs008Xml(new("NBGEGE22")).Build(ValidatedPacs008.Validate(Request(), Policy).Payment!,
                new(identifiers.MessageId, identifiers.TransactionId, Created));
            preparation.StageUnsignedXml(payment, claim, unsigned, Now);
            await intake.Unit.SaveAsync();
        }
        string signed;
        await using (var preparationScope = database.Session())
        {
            var payment = (await preparationScope.Payments.FindAsync(paymentId, default))!;
            var preparation = new PaymentPreparationRepository(preparationScope.Context);
            var stored = (await preparation.ReadAsync(paymentId, default))!;
            var result = Signer().Prepare(stored.UnsignedXml!, certificate);
            Assert.True(result.IsSigned);
            signed = result.Xml;
            preparation.StageSignedXml(payment, claim, signed, Now);
            await preparationScope.Unit.SaveAsync();
        }
        await using var read = database.Session();
        var reloaded = (await new PaymentPreparationRepository(read.Context).ReadAsync(paymentId, default))!;
        Assert.Equal(unsigned, reloaded.UnsignedXml);
        Assert.Equal(signed, reloaded.SignedXml);
        Assert.True(await JavaSignatureVerifier.VerifyAsync(reloaded.SignedXml!, certificate));
    }

    [Theory]
    [InlineData("content-commitment")]
    [InlineData("p384")]
    public async Task Other_signing_capable_ec_certificates_remain_supported(string kind)
    {
        using var certificate = Certificate(kind);
        var result = Signer().Prepare(Unsigned(), certificate);
        Assert.True(await JavaSignatureVerifier.VerifyAsync(result.Xml, certificate));
        if (kind == "p384")
            Assert.Equal(96, Convert.FromBase64String(XDocument.Parse(result.Xml).Descendants(Ds + "SignatureValue").Single().Value).Length);
    }

    private static Pacs008MessageSigner Signer(bool allowUnsigned = false) => new(new(allowUnsigned, isDevelopment: true), new FixedClock());
    private static string Unsigned() => new Pacs008Xml(new("NBGEGE22"))
        .Build(ValidatedPacs008.Validate(Request(), Policy).Payment!, new("stored-message", "stored-transaction", Created));

    private static X509Certificate2 Certificate(string kind = "valid")
    {
        using var ec = ECDsa.Create(kind == "p384" ? ECCurve.NamedCurves.nistP384 : ECCurve.NamedCurves.nistP256);
        using var rsa = RSA.Create(2048);
        var request = kind == "rsa"
            ? new CertificateRequest("CN=IPS.Signing.Tests", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            : new CertificateRequest("CN=IPS.Signing.Tests", ec, HashAlgorithmName.SHA256);
        var keyUsage = kind switch
        {
            "wrong-usage" => X509KeyUsageFlags.KeyCertSign,
            "content-commitment" => X509KeyUsageFlags.NonRepudiation,
            _ => X509KeyUsageFlags.DigitalSignature
        };
        request.CertificateExtensions.Add(new X509KeyUsageExtension(keyUsage, critical: true));
        var start = kind == "future" ? Now.AddDays(1) : Now.AddDays(-2);
        var end = kind == "expired" ? Now.AddDays(-1) : Now.AddDays(2);
        var certificate = request.CreateSelfSigned(start, end);
        if (kind != "public-only") return certificate;
        using (certificate) return X509CertificateLoader.LoadCertificate(certificate.RawData);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
