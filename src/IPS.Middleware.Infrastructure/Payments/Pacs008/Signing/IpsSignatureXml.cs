using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

internal static class IpsSignatureXml
{
    internal const string EcdsaSha256 = "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256";
    internal const string CanonicalXml11 = "http://www.w3.org/2006/12/xml-c14n11";
    private const string SignatureId = "MONT";

    internal static XmlElement Create(XmlDocument document, byte[] digest, X509Certificate2 certificate)
    {
        var signature = document.CreateElement("ds", "Signature", SignedXml.XmlDsigNamespaceUrl);
        signature.SetAttribute("Id", SignatureId);
        signature.SetAttribute("xmlns:ds", SignedXml.XmlDsigNamespaceUrl);
        var signedInfo = Append(signature, "SignedInfo");
        Append(signedInfo, "CanonicalizationMethod").SetAttribute("Algorithm", CanonicalXml11);
        Append(signedInfo, "SignatureMethod").SetAttribute("Algorithm", EcdsaSha256);

        var reference = new Reference { Uri = "", DigestMethod = SignedXml.XmlDsigSHA256Url, DigestValue = digest };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        signedInfo.AppendChild(document.ImportNode(reference.GetXml(), deep: true));
        Append(signature, "SignatureValue");

        var certificateData = new KeyInfoX509Data();
        certificateData.AddSubjectName(certificate.SubjectName.Name);
        certificateData.AddCertificate(certificate);
        var keyInfo = new KeyInfo();
        keyInfo.AddClause(certificateData);
        signature.AppendChild(document.ImportNode(keyInfo.GetXml(), deep: true));
        return signature;
    }

    internal static void SetSignatureValue(XmlElement signature, byte[] value) =>
        signature.GetElementsByTagName("SignatureValue", SignedXml.XmlDsigNamespaceUrl)[0]!.InnerText = Convert.ToBase64String(value);

    private static XmlElement Append(XmlElement parent, string name)
    {
        var element = parent.OwnerDocument!.CreateElement("ds", name, SignedXml.XmlDsigNamespaceUrl);
        parent.AppendChild(element);
        return element;
    }
}
