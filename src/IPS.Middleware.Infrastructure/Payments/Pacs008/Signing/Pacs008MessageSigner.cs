using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

public sealed class Pacs008MessageSigner(Pacs008SigningPolicy policy, TimeProvider clock)
{
    public Pacs008SigningResult Prepare(string unsignedXml, X509Certificate2? certificate) =>
        Sign(unsignedXml, certificate, Pacs008Schema.Validate);

    public Pacs008SigningResult PreparePacs009(string unsignedXml, X509Certificate2? certificate) =>
        Sign(unsignedXml, certificate, Pacs008Schema.ValidatePacs009);

    public Pacs008SigningResult PreparePacs004(string unsignedXml, X509Certificate2? certificate) =>
        Sign(unsignedXml, certificate, Pacs008Schema.ValidatePacs004);

    public Pacs008SigningResult PrepareCamt056(string unsignedXml, X509Certificate2? certificate) =>
        Sign(unsignedXml, certificate, Pacs008Schema.ValidateCamt056);

    public Pacs008SigningResult PrepareReply(string unsignedXml, X509Certificate2? certificate) =>
        Sign(unsignedXml, certificate, xml => Pacs008Schema.ValidateReply(xml));

    public Pacs008SigningResult PrepareInvestigation(string unsignedXml, X509Certificate2? certificate) =>
        Sign(unsignedXml, certificate, Pacs008Schema.ValidateInvestigation);

    private Pacs008SigningResult Sign(string unsignedXml, X509Certificate2? certificate, Action<string> validate)
    {
        var document = ReadUnsignedMessage(unsignedXml, validate);
        if (certificate is null)
        {
            if (!policy.AllowUnsignedWithoutCertificate)
            {
                throw new SigningCertificateException("A signing certificate is required.");
            }

            return new(unsignedXml, IsSigned: false);
        }

        ValidateCertificate(certificate);
        using var key = certificate.GetECDsaPrivateKey()
            ?? throw new SigningCertificateException("Signing requires a certificate with an ECDSA private key.");
        var header = document.DocumentElement!.ChildNodes.OfType<XmlElement>()
            .Single(element => element.LocalName == "AppHdr" && element.NamespaceURI == Pacs008Xml.HeaderNamespace);
        SignedInfoCanonicalization.CheckAncestorAttributes(header);
        var envelope = document.CreateElement(header.Prefix, "Sgntr", Pacs008Xml.HeaderNamespace);
        header.AppendChild(envelope);
        // The new Sgntr is empty here: enveloped-signature removal leaves this exact document.
        var digest = SHA256.HashData(SignedInfoCanonicalization.CanonicalizeInclusive10WithoutComments(document));
        var signature = IpsSignatureXml.Create(document, digest, certificate);
        envelope.AppendChild(signature);
        var signedInfo = (XmlElement)signature.FirstChild!;
        var canonicalSignedInfo = SignedInfoCanonicalization.Canonicalize(signedInfo);
        var signatureBytes = key.SignData(canonicalSignedInfo, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        IpsSignatureXml.SetSignatureValue(signature, signatureBytes);
        var signedXml = document.OuterXml;
        validate(signedXml);
        return new(signedXml, IsSigned: true);
    }

    private static XmlDocument ReadUnsignedMessage(string xml, Action<string> validate)
    {
        validate(xml);
        using var reader = XmlReader.Create(new StringReader(xml), Pacs008Schema.SafeReader);
        var document = new XmlDocument
        {
            PreserveWhitespace = true,
            XmlResolver = null
        };
        document.Load(reader);
        if (document.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl).Count != 0 ||
            document.GetElementsByTagName("Sgntr", Pacs008Xml.HeaderNamespace).Count != 0)
        {
            throw new InvalidOperationException("Expected unsigned XML. Reuse an existing signed artifact rather than signing it again.");
        }

        return document;
    }

    private void ValidateCertificate(X509Certificate2 certificate)
    {
        var now = clock.GetUtcNow();
        if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
        {
            throw new SigningCertificateException("The signing certificate is outside its validity period.");
        }

        if (!certificate.HasPrivateKey)
        {
            throw new SigningCertificateException("The signing certificate has no private key.");
        }

        var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().SingleOrDefault();
        const X509KeyUsageFlags signingUsages = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation;
        if (usage is not null && (usage.KeyUsages & signingUsages) == 0)
        {
            throw new SigningCertificateException("The certificate does not permit digital signatures.");
        }
    }
}

public sealed record Pacs008SigningResult(string Xml, bool IsSigned);

// No usable signing certificate is available; replacing the certificate can resolve it.
public sealed class SigningCertificateException(string message) : InvalidOperationException(message);
