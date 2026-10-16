using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Linq;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Payments;

/// <summary>Independent pacs.002 reply fixtures. Text templates follow Annex D 8.1.8, signed in process with the IPS profile.</summary>
internal static class IpsReplies
{
    private const string HeaderNamespace = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";
    private const string Pacs028Namespace = "urn:iso:std:iso:20022:tech:xsd:pacs.028.001.06";
    private static readonly DateTimeOffset EarliestTestClock = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal sealed record Reply
    {
        public required string MessageId { get; init; }
        public required string TransactionId { get; init; }
        public required string EndToEndId { get; init; }
        public string? GroupStatus { get; init; } = "ACCP";
        public string? TransactionStatus { get; init; } = "ACCP";
        public string OriginalMessageName { get; init; } = "pacs.008.001.12";
        public string? ReasonCode { get; init; }
        public string? AdditionalInformation { get; init; }
        public bool IncludeTransaction { get; init; } = true;
    }

    internal static string Unsigned(Reply reply)
    {
        static string Status(string element, string? value) => value is null ? "" : $"<pacs:{element}>{value}</pacs:{element}>";
        var reason = reply.ReasonCode is null && reply.AdditionalInformation is null ? "" :
            "<pacs:StsRsnInf>" + (reply.ReasonCode is null ? "" : $"<pacs:Rsn><pacs:Cd>{reply.ReasonCode}</pacs:Cd></pacs:Rsn>") +
            (reply.AdditionalInformation is null ? "" : $"<pacs:AddtlInf>{SecurityElement.Escape(reply.AdditionalInformation)}</pacs:AddtlInf>") +
            "</pacs:StsRsnInf>";
        var transaction = !reply.IncludeTransaction ? "" :
            "<pacs:TxInfAndSts><pacs:StsId>IPS-STS-1</pacs:StsId>" +
            $"<pacs:OrgnlEndToEndId>{reply.EndToEndId}</pacs:OrgnlEndToEndId><pacs:OrgnlTxId>{reply.TransactionId}</pacs:OrgnlTxId>" +
            Status("TxSts", reply.TransactionStatus) + reason + "<pacs:AccptncDtTm>2026-10-04T14:00:01.000Z</pacs:AccptncDtTm></pacs:TxInfAndSts>";
        return "<Message xmlns:head=\"urn:iso:std:iso:20022:tech:xsd:head.001.001.03\" xmlns:pacs=\"urn:iso:std:iso:20022:tech:xsd:pacs.002.001.14\">" +
            "<head:AppHdr><head:Fr><head:FIId><head:FinInstnId><head:BICFI>NBGEGE22</head:BICFI></head:FinInstnId></head:FIId></head:Fr>" +
            "<head:To><head:FIId><head:FinInstnId><head:BICFI>BAGAGE22</head:BICFI></head:FinInstnId></head:FIId></head:To>" +
            "<head:BizMsgIdr>IPS-REPLY-1</head:BizMsgIdr><head:MsgDefIdr>pacs.002.001.14</head:MsgDefIdr>" +
            "<head:CreDt>2026-10-04T14:00:01.000Z</head:CreDt><head:Sgntr/></head:AppHdr>" +
            "<pacs:Document><pacs:FIToFIPmtStsRpt><pacs:GrpHdr><pacs:MsgId>IPS-REPLY-1</pacs:MsgId><pacs:CreDtTm>2026-10-04T14:00:01.000Z</pacs:CreDtTm></pacs:GrpHdr>" +
            $"<pacs:OrgnlGrpInfAndSts><pacs:OrgnlMsgId>{reply.MessageId}</pacs:OrgnlMsgId><pacs:OrgnlMsgNmId>{reply.OriginalMessageName}</pacs:OrgnlMsgNmId>" +
            Status("GrpSts", reply.GroupStatus) + (reply.IncludeTransaction ? "" : reason) + "</pacs:OrgnlGrpInfAndSts>" +
            transaction + "</pacs:FIToFIPmtStsRpt></pacs:Document></Message>";
    }

    // ACCP/RJCT report the original payment; 1016/1017 reject the inquiry itself (not found / still processing).
    internal static async Task<IpsSubmissionResponse> AnswerInvestigationAsync(X509Certificate2 ips, string pacs028, string answer)
    {
        XNamespace ns = Pacs028Namespace;
        var document = XDocument.Parse(pacs028);
        string Value(string name) => document.Descendants(ns + name).Single().Value;
        var inquiry = answer is "1016" or "1017";
        var reply = new Reply
        {
            MessageId = inquiry ? Value("MsgId") : Value("OrgnlMsgId"),
            TransactionId = Value("OrgnlTxId"),
            EndToEndId = Value("OrgnlEndToEndId"),
            OriginalMessageName = inquiry ? "pacs.028.001.06" : "pacs.008.001.12",
            IncludeTransaction = !inquiry,
            GroupStatus = inquiry ? "RJCT" : answer,
            TransactionStatus = inquiry ? "RJCT" : answer,
            ReasonCode = inquiry ? "AG09" : answer == "RJCT" ? "AC01" : null
        };
        var body = (await SignAsync(ips, Unsigned(reply)))[0];
        return new(200, body, [new("X-MONTRAN-IPS-ReqSts", inquiry ? "RJCT/" + answer : answer)]);
    }

    internal static bool IsInvestigation(string xml) => xml.Contains(Pacs028Namespace, StringComparison.Ordinal);

    // Signatures are verified at the test's clock (012b), so the default validity covers both the fixed test clocks
    // (October 2026) and the real current time.
    internal static X509Certificate2 Certificate(string subject = "CN=Simulated IPS", DateTimeOffset? validAt = null)
    {
        var at = validAt ?? DateTimeOffset.UtcNow;
        var start = at < EarliestTestClock ? at : EarliestTestClock;
        return Certificate(subject, start.AddDays(-1), at.AddYears(1));
    }

    internal static X509Certificate2 Certificate(string subject, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    /// <summary>Signs each message with the IPS profile in its empty AppHdr/Sgntr; certificates must carry an ECDSA private key.</summary>
    internal static Task<string[]> SignAsync(X509Certificate2 certificate, params string[] messages) =>
        Task.FromResult(messages.Select(message => Sign(certificate, message)).ToArray());

    private static string Sign(X509Certificate2 certificate, string xml)
    {
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using (var reader = XmlReader.Create(new StringReader(xml), Pacs008Schema.SafeReader))
        {
            document.Load(reader);
        }

        var envelope = Assert.Single(document.GetElementsByTagName("Sgntr", HeaderNamespace).OfType<XmlElement>());
        Assert.False(envelope.HasChildNodes, "Expected one empty AppHdr/Sgntr");
        // The enveloped-signature transform removes ds:Signature, leaving this exact document with an empty Sgntr.
        var digest = SHA256.HashData(SignedInfoCanonicalization.CanonicalizeInclusive10WithoutComments(document));
        var signature = IpsSignatureXml.Create(document, digest, certificate);
        envelope.AppendChild(signature);
        using var key = certificate.GetECDsaPrivateKey()!;
        var signedInfo = SignedInfoCanonicalization.Canonicalize((XmlElement)signature.FirstChild!);
        IpsSignatureXml.SetSignatureValue(signature, key.SignData(signedInfo, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        return document.OuterXml;
    }
}
