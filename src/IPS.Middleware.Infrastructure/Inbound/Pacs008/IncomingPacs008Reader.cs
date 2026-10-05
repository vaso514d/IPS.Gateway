using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Inbound.Pacs008;

public sealed class IncomingPacs008Reader
{
    private static readonly XNamespace Head = Pacs008Xml.HeaderNamespace;
    private static readonly XNamespace Pacs = Pacs008Xml.DocumentNamespace;

    /// <summary>Ready for a trusted, valid single payment; FF01 Reject for a trusted count/batch violation; otherwise Hold.</summary>
    public IncomingPacs008ReadResult Read(string xml, IReadOnlyCollection<X509Certificate2> trustedCertificates)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), Pacs008Schema.SafeReader);
            var root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root;
            if (root is null || root.Name != "Message" || root.Elements().ToArray() is not [var header, var body] ||
                header.Name != Head + "AppHdr" || body.Name != Pacs + "Document")
            {
                return Hold("Unexpected message envelope or version.");
            }

            if (header.Element(Head + "MsgDefIdr")?.Value != Pacs008Message.MessageDefinition)
            {
                return Hold("Unsupported message definition.");
            }
            // Nothing in the message is trusted, including its correlation, before the original signature verifies.
            if (!IpsSignatureVerifier.IsTrusted(xml, trustedCertificates))
            {
                return Hold("Untrusted message signature.");
            }

            var transfer = body.Element(Pacs + "FIToFICstmrCdtTrf");
            var group = transfer?.Element(Pacs + "GrpHdr");
            var transactions = transfer?.Elements(Pacs + "CdtTrfTxInf").ToArray() ?? [];
            if (group is null || transactions.Length == 0)
            {
                return Hold("Missing payment correlation.");
            }

            var single = transactions.Length == 1 &&
                int.TryParse(group.Element(Pacs + "NbOfTxs")?.Value, CultureInfo.InvariantCulture, out var count) && count == 1;
            if (single)
            {
                Pacs008Schema.Validate(xml);
            }
            else
            {
                ValidateEachTransferAlone(root.Document!);
            }

            var original = IncomingPacs008Mapping.Original(header, group, transactions[0]);
            return single
                ? new IncomingPacs008ReadResult.Ready(new(IncomingPacs008Mapping.Payment(group, transactions[0]), original))
                : new IncomingPacs008ReadResult.Reject(original, "FF01", "pacs.008 must contain exactly one credit transfer and NbOfTxs = 1.");
        }
        catch (Exception error) when (error is XmlException or XmlSchemaException or FormatException or OverflowException or InvalidOperationException)
        {
            return Hold("Malformed or unsupported payment content.");
        }
    }

    // Only a count/batch violation may become FF01: each transfer, kept in its original position, must be schema-valid
    // in a temporary single-payment copy. Copies are never used for signature trust or submission.
    private static void ValidateEachTransferAlone(XDocument original)
    {
        var transfers = Transfer(original).Elements(Pacs + "CdtTrfTxInf").Count();
        for (var index = 0; index < transfers; index++)
        {
            var candidate = new XDocument(original);
            var transfer = Transfer(candidate);
            var group = transfer.Element(Pacs + "GrpHdr")!;
            if (group.Element(Pacs + "NbOfTxs") is { } count)
            {
                count.Value = "1";
            }
            else
            {
                (group.Element(Pacs + "CreDtTm") ?? throw new FormatException("Missing creation time.")).AddAfterSelf(new XElement(Pacs + "NbOfTxs", "1"));
            }

            transfer.Elements(Pacs + "CdtTrfTxInf").Where((_, position) => position != index).Remove();
            Pacs008Schema.Validate(candidate.ToString(SaveOptions.DisableFormatting));
        }
    }

    private static XElement Transfer(XDocument document) => document.Root!.Element(Pacs + "Document")!.Element(Pacs + "FIToFICstmrCdtTrf")!;

    private static IncomingPacs008ReadResult Hold(string reason) => new IncomingPacs008ReadResult.Hold(reason);
}
