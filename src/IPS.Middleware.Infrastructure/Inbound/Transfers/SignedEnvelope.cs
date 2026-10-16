using System.Xml;
using System.Xml.Linq;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Inbound.Transfers;

// The checks every incoming transfer reader makes first, in this order: the IPS envelope with the expected document
// namespace, the message definition, and the IPS signature as of the receipt (012b). Nothing in the message is trusted before
// they pass. A malformed document throws an XmlException, which the reader holds like any other malformed content.
internal static class SignedEnvelope
{
    private static readonly XNamespace Head = Pacs008Xml.HeaderNamespace;

    internal static OpenedEnvelope Open(
        string xml, DateTimeOffset receivedAtUtc, IpsSignatureTrust trust, XNamespace document, Func<string, bool> isDefinition)
    {
        using var reader = XmlReader.Create(new StringReader(xml), Pacs008Schema.SafeReader);
        var root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root;
        if (root is null || root.Name != "Message" || root.Elements().ToArray() is not [var header, var body] ||
            header.Name != Head + "AppHdr" || body.Name != document + "Document")
        {
            return Held("Unexpected message envelope or version.");
        }

        if (!isDefinition(header.Element(Head + "MsgDefIdr")?.Value ?? string.Empty))
        {
            return Held("Unsupported message definition.");
        }

        return trust.Check(xml, receivedAtUtc) switch
        {
            IpsSignatureCheck.Trusted => new OpenedEnvelope(body, null),
            IpsSignatureCheck.OutsideValidity outside => Held($"IPS certificate outside its validity period: {outside.Detail}"),
            _ => Held("Untrusted message signature.")
        };
    }

    private static OpenedEnvelope Held(string reason) => new(null, reason);
}

// The verified Document element, or why the message is held.
internal sealed record OpenedEnvelope(XElement? Document, string? HoldReason);
