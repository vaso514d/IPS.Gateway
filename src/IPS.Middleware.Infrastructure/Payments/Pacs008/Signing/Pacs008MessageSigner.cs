using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

public sealed class Pacs008MessageSigner(Pacs008SigningPolicy policy, TimeProvider clock)
{
    public Pacs008SigningResult Prepare(string unsignedXml, X509Certificate2? certificate)
    {
        var document = ReadUnsignedMessage(unsignedXml);
        if (certificate is null)
        {
            if (!policy.AllowUnsignedWithoutCertificate)
                throw new InvalidOperationException("A signing certificate is required.");
            return new(unsignedXml, IsSigned: false);
        }

        ValidateCertificate(certificate);
        using var key = certificate.GetECDsaPrivateKey()
            ?? throw new InvalidOperationException("Signing requires a certificate with an ECDSA private key.");
        var header = document.DocumentElement!.ChildNodes.OfType<XmlElement>()
            .Single(element => element.LocalName == "AppHdr" && element.NamespaceURI == Pacs008Xml.HeaderNamespace);
        SignedInfoCanonicalization.CheckAncestorAttributes(header);
        var envelope = document.CreateElement(header.Prefix, "Sgntr", Pacs008Xml.HeaderNamespace);
        header.AppendChild(envelope);

        var digest = SHA256.HashData(CanonicalizeContent(document));
        var signature = IpsSignatureXml.Create(document, digest, certificate);
        envelope.AppendChild(signature);
        var signedInfo = (XmlElement)signature.FirstChild!;
        var canonicalSignedInfo = SignedInfoCanonicalization.Canonicalize(signedInfo);
        var signatureBytes = key.SignData(canonicalSignedInfo, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        IpsSignatureXml.SetSignatureValue(signature, signatureBytes);

        var signedXml = document.OuterXml;
        Pacs008Schema.Validate(signedXml);
        return new(signedXml, IsSigned: true);
    }

    private static XmlDocument ReadUnsignedMessage(string xml)
    {
        Pacs008Schema.Validate(xml);
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        document.Load(reader);
        if (document.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl).Count != 0 ||
            document.GetElementsByTagName("Sgntr", Pacs008Xml.HeaderNamespace).Count != 0)
            throw new InvalidOperationException("Expected unsigned XML. Reuse an existing signed artifact rather than signing it again.");
        return document;
    }

    private void ValidateCertificate(X509Certificate2 certificate)
    {
        var now = clock.GetUtcNow();
        if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
            throw new InvalidOperationException("The signing certificate is outside its validity period.");
        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException("The signing certificate has no private key.");
        var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().SingleOrDefault();
        const X509KeyUsageFlags signingUsages = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation;
        if (usage is not null && (usage.KeyUsages & signingUsages) == 0)
            throw new InvalidOperationException("The certificate does not permit digital signatures.");
    }

    private static byte[] CanonicalizeContent(XmlDocument document)
    {
        // The new Sgntr is empty here: enveloped-signature removal leaves this exact document.
        var transform = new XmlDsigC14NTransform(includeComments: false);
        transform.LoadInput(document);
        using var output = (Stream)transform.GetOutput(typeof(Stream));
        using var bytes = new MemoryStream();
        output.CopyTo(bytes);
        return bytes.ToArray();
    }
}

public sealed record Pacs008SigningResult(string Xml, bool IsSigned);
