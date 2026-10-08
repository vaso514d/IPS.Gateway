using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

// Verifies the IPS enveloped signature profile only: one ds:Signature in AppHdr/Sgntr, C14N1.1 SignedInfo,
// a whole-document reference with enveloped + C14N1.0 transforms, SHA-256 and ECDSA-SHA256, signed by a trusted IPS
// certificate that is within its validity period at the given time. Anything else is untrusted; a signature that verifies
// with a trusted certificate outside its validity period is reported as such.
internal static class IpsSignatureVerifier
{
    private const string Ds = SignedXml.XmlDsigNamespaceUrl;
    private static readonly string[] ReferenceTransforms = [SignedXml.XmlDsigEnvelopedSignatureTransformUrl, SignedXml.XmlDsigC14NTransformUrl];
    private static readonly IpsSignatureCheck Trusted = new IpsSignatureCheck.Trusted();
    private static readonly IpsSignatureCheck Untrusted = new IpsSignatureCheck.Untrusted();

    internal static IpsSignatureCheck Check(string xml, IReadOnlyCollection<X509Certificate2> trusted, DateTimeOffset at)
    {
        try
        {
            return Verify(xml, trusted, at);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or XmlException or InvalidOperationException)
        {
            return Untrusted;
        }
    }

    private static IpsSignatureCheck Verify(string xml, IReadOnlyCollection<X509Certificate2> trusted, DateTimeOffset at)
    {
        var document = Load(xml);
        if (SingleSignature(document) is not { } signature)
        {
            return Untrusted;
        }

        var names = new XmlNamespaceManager(document.NameTable);
        names.AddNamespace("ds", Ds);
        if (signature.SelectSingleNode("ds:SignedInfo", names) is not XmlElement signedInfo ||
            signedInfo.ChildNodes.OfType<XmlElement>().Count() != 3 ||
            Algorithm(signedInfo, "ds:CanonicalizationMethod", names) != IpsSignatureXml.CanonicalXml11 ||
            Algorithm(signedInfo, "ds:SignatureMethod", names) != IpsSignatureXml.EcdsaSha256 ||
            signedInfo.SelectNodes("ds:Reference", names) is not { Count: 1 } references ||
            references[0] is not XmlElement { } reference || reference.GetAttribute("URI") != "" ||
            !reference.SelectNodes("ds:Transforms/ds:Transform", names)!.OfType<XmlElement>()
                .Select(transform => transform.GetAttribute("Algorithm")).SequenceEqual(ReferenceTransforms) ||
            Algorithm(reference, "ds:DigestMethod", names) != SignedXml.XmlDsigSHA256Url)
        {
            return Untrusted;
        }

        // Annex C 2.6: IPS does not embed its certificate; the signature names it by issuer and serial number.
        var candidates = Candidates(signature, names, trusted);
        if (candidates.Count == 0)
        {
            return Untrusted;
        }

        // The enveloped-signature transform removes ds:Signature; the reference covers the rest of the document.
        var unsigned = Load(xml);
        var removed = SingleSignature(unsigned)!;
        removed.ParentNode!.RemoveChild(removed);
        var digest = SHA256.HashData(SignedInfoCanonicalization.CanonicalizeInclusive10WithoutComments(unsigned));
        if (!CryptographicOperations.FixedTimeEquals(digest, Convert.FromBase64String(Text(reference, "ds:DigestValue", names))))
        {
            return Untrusted;
        }

        var canonicalSignedInfo = SignedInfoCanonicalization.Canonicalize(signedInfo);
        var signatureValue = Convert.FromBase64String(Text(signature, "ds:SignatureValue", names));
        foreach (var certificate in candidates)
        {
            using var key = certificate.GetECDsaPublicKey();
            if (key is null || !key.VerifyData(canonicalSignedInfo, signatureValue, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                continue;
            }

            // Dates first and compact: the stored hold reason is short, and the period matters more than the subject.
            return IsValidAt(certificate, at)
                ? Trusted
                : new IpsSignatureCheck.OutsideValidity(string.Create(CultureInfo.InvariantCulture,
                    $"valid {certificate.NotBefore.ToUniversalTime():yyyy-MM-dd'T'HH':'mm':'ss'Z'} to {certificate.NotAfter.ToUniversalTime():yyyy-MM-dd'T'HH':'mm':'ss'Z'}, {certificate.Subject}"));
        }

        return Untrusted;
    }

    // An embedded certificate must be one of ours byte for byte; otherwise X509IssuerSerial picks it; a signature that names
    // no certificate is tried against every trusted one, as the central system does with a participant's list (Annex C 2.2).
    private static IReadOnlyList<X509Certificate2> Candidates(XmlElement signature, XmlNamespaceManager names, IReadOnlyCollection<X509Certificate2> trusted)
    {
        if (signature.SelectSingleNode("ds:KeyInfo/ds:X509Data/ds:X509Certificate", names) is { } embedded)
        {
            var presented = Convert.FromBase64String(embedded.InnerText);
            return trusted.Where(candidate => candidate.RawData.AsSpan().SequenceEqual(presented)).ToArray();
        }

        if (signature.SelectSingleNode("ds:KeyInfo/ds:X509Data/ds:X509IssuerSerial", names) is XmlElement issuerSerial)
        {
            var issuer = Text(issuerSerial, "ds:X509IssuerName", names);
            var serial = BigInteger.Parse(Text(issuerSerial, "ds:X509SerialNumber", names).Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
            return trusted.Where(candidate => SerialOf(candidate) == serial && SameName(candidate.IssuerName, issuer)).ToArray();
        }

        return trusted.ToArray();
    }

    private static BigInteger SerialOf(X509Certificate2 certificate) =>
        new(certificate.SerialNumberBytes.Span, isUnsigned: true, isBigEndian: true);

    // The issuer is text (RFC 2253 in Java, comma-space separated in .NET); compare attribute by attribute, ignoring
    // spacing and case, rather than encodings that differ between producers.
    private static bool SameName(X500DistinguishedName certificateIssuer, string issuer)
    {
        try
        {
            return Attributes(certificateIssuer).SequenceEqual(Attributes(new X500DistinguishedName(issuer)));
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static IEnumerable<string> Attributes(X500DistinguishedName name) => name.EnumerateRelativeDistinguishedNames()
        .Select(attribute => attribute.GetSingleElementType().Value + "=" + attribute.GetSingleElementValue()?.Trim().ToUpperInvariant())
        .Order(StringComparer.Ordinal);

    private static bool IsValidAt(X509Certificate2 certificate, DateTimeOffset at) =>
        certificate.NotBefore.ToUniversalTime() <= at && at <= certificate.NotAfter.ToUniversalTime();

    private static XmlElement? SingleSignature(XmlDocument document) =>
        document.GetElementsByTagName("Signature", Ds) is { Count: 1 } signatures &&
        signatures[0] is XmlElement { ParentNode: XmlElement { LocalName: "Sgntr", NamespaceURI: Pacs008Xml.HeaderNamespace } envelope } signature &&
        envelope.ParentNode is XmlElement { LocalName: "AppHdr", NamespaceURI: Pacs008Xml.HeaderNamespace }
            ? signature : null;

    private static string? Algorithm(XmlElement parent, string path, XmlNamespaceManager names) =>
        (parent.SelectSingleNode(path, names) as XmlElement)?.GetAttribute("Algorithm");

    private static string Text(XmlElement parent, string path, XmlNamespaceManager names) =>
        parent.SelectSingleNode(path, names)?.InnerText ?? throw new FormatException($"Missing {path}.");

    private static XmlDocument Load(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), Pacs008Schema.SafeReader);
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        document.Load(reader);
        return document;
    }
}
