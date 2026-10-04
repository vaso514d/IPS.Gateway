using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

/// <summary>
/// Verifies the IPS enveloped signature profile only: one ds:Signature in AppHdr/Sgntr, C14N1.1 SignedInfo,
/// a whole-document reference with enveloped + C14N1.0 transforms, SHA-256 and ECDSA-SHA256, signed by a
/// certificate byte-identical to a trusted IPS certificate. Anything else is untrusted.
/// </summary>
internal static class IpsSignatureVerifier
{
    private const string Ds = SignedXml.XmlDsigNamespaceUrl;
    private static readonly string[] ReferenceTransforms = [SignedXml.XmlDsigEnvelopedSignatureTransformUrl, SignedXml.XmlDsigC14NTransformUrl];

    internal static bool IsTrusted(string xml, IReadOnlyCollection<X509Certificate2> trusted)
    {
        try { return Verify(xml, trusted); }
        catch (Exception exception) when (exception is CryptographicException or FormatException or XmlException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool Verify(string xml, IReadOnlyCollection<X509Certificate2> trusted)
    {
        var document = Load(xml);
        if (SingleSignature(document) is not { } signature) return false;
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
            return false;

        var presented = Convert.FromBase64String(Text(signature, "ds:KeyInfo/ds:X509Data/ds:X509Certificate", names));
        var certificate = trusted.FirstOrDefault(candidate => candidate.RawData.AsSpan().SequenceEqual(presented));
        using var key = certificate?.GetECDsaPublicKey();
        if (key is null) return false;

        // The enveloped-signature transform removes ds:Signature; the reference covers the rest of the document.
        var unsigned = Load(xml);
        var removed = SingleSignature(unsigned)!;
        removed.ParentNode!.RemoveChild(removed);
        var digest = SHA256.HashData(SignedInfoCanonicalization.CanonicalizeInclusive10WithoutComments(unsigned));
        if (!CryptographicOperations.FixedTimeEquals(digest, Convert.FromBase64String(Text(reference, "ds:DigestValue", names))))
            return false;
        return key.VerifyData(SignedInfoCanonicalization.Canonicalize(signedInfo),
            Convert.FromBase64String(Text(signature, "ds:SignatureValue", names)),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

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
