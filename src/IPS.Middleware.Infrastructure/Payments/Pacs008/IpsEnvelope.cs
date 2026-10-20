using System.Xml.Linq;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008;

// The IPS message envelope on the wire (Annex D 3.1, p. 129, and every message the IPS UAT sends):
//   <env:Message xmlns:env="urn:montran:message.01">
//     <env:AppHdr xmlns="...head.001.001.03"> Fr, To, BizMsgIdr, MsgDefIdr, CreDt, Sgntr </env:AppHdr>
//     <env:FIToFICstmrCdtTrf xmlns="...pacs.008.001.12"> GrpHdr, CdtTrfTxInf </env:FIToFICstmrCdtTrf>
//   </env:Message>
// The envelope elements are in the montran namespace and their content in the ISO 20022 namespaces; there is no ISO
// Document element. Builders and XSD validation work with the ISO AppHdr and Document, so this converts between the two.
internal static class IpsEnvelope
{
    internal const string Namespace = "urn:montran:message.01";
    private const string Prefix = "env";
    private static readonly XNamespace Env = Namespace;
    private static readonly XNamespace Head = Pacs008Xml.HeaderNamespace;

    // The wire message for a head.001 AppHdr and an ISO Document with one business element.
    internal static XElement Wrap(XElement header, XElement document)
    {
        var business = document.Elements().Single();
        return new XElement(Env + "Message",
            new XAttribute(XNamespace.Xmlns + Prefix, Namespace),
            new XElement(Env + "AppHdr", new XAttribute("xmlns", Head.NamespaceName), header.Attributes(), header.Nodes()),
            new XElement(Env + business.Name.LocalName,
                new XAttribute("xmlns", business.Name.NamespaceName), business.Attributes(), business.Nodes()));
    }

    // The ISO AppHdr and Document of a wire message whose business content is in documentNamespace, or null when the root
    // is not that envelope.
    internal static IsoMessage? Open(XElement? root, XNamespace documentNamespace)
    {
        if (root?.Name != Env + "Message" || root.Elements().ToArray() is not [var header, var business] ||
            header.Name != Env + "AppHdr" || business.Name.Namespace != Env || !HasContentIn(header, Head) ||
            !HasContentIn(business, documentNamespace))
        {
            return null;
        }

        return new IsoMessage(
            new XElement(Head + "AppHdr", Content(header)),
            new XElement(documentNamespace + "Document", new XElement(documentNamespace + business.Name.LocalName, Content(business))));
    }

    // The business content (GrpHdr...) carries the ISO namespace; the envelope element itself does not.
    private static bool HasContentIn(XElement element, XNamespace content) =>
        element.Elements().Any() && element.Elements().All(child => child.Name.Namespace == content);

    private static IEnumerable<object> Content(XElement element) =>
        element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).Cast<object>().Concat(element.Nodes());
}

// A wire message read as its ISO 20022 parts.
internal sealed record IsoMessage(XElement Header, XElement Document);
