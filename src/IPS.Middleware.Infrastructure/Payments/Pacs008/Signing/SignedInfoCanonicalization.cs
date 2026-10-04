using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.XPath;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

internal static class SignedInfoCanonicalization
{
    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";

    internal static void CheckAncestorAttributes(XmlElement element)
    {
        for (XmlNode? node = element; node is XmlElement ancestor; node = node.ParentNode)
        {
            if (ancestor.Attributes.Cast<XmlAttribute>().Any(attribute => attribute.NamespaceURI == XmlNamespace))
                throw new InvalidOperationException("This signing profile does not support xml:* attributes on SignedInfo or its ancestors.");
        }
    }

    internal static byte[] Canonicalize(XmlElement signedInfo)
    {
        CheckAncestorAttributes(signedInfo);
        // This freshly generated subtree has no xml:* attributes. C14N1.0 and C14N1.1
        // agree in this enforced subset. Include inherited namespaces for inclusive C14N.
        var isolated = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        var root = (XmlElement)isolated.ImportNode(signedInfo, deep: true);
        isolated.AppendChild(root);
        var namespaces = signedInfo.CreateNavigator()!.GetNamespacesInScope(XmlNamespaceScope.All);
        foreach (var (prefix, uri) in namespaces)
        {
            if (prefix == "xml") continue;
            root.SetAttribute(prefix.Length == 0 ? "xmlns" : "xmlns:" + prefix, uri);
        }
        var transform = new XmlDsigC14NTransform(includeComments: false);
        transform.LoadInput(isolated);
        using var output = (Stream)transform.GetOutput(typeof(Stream));
        using var bytes = new MemoryStream();
        output.CopyTo(bytes);
        return bytes.ToArray();
    }
}
