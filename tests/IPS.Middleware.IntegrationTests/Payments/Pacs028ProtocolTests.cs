using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Investigation;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using Xunit;
using static IPS.Middleware.IntegrationTests.Payments.IpsReplies;
using static IPS.Middleware.IntegrationTests.Payments.Pacs008Fixture;

namespace IPS.Middleware.IntegrationTests.Payments;

public sealed class Pacs028ProtocolTests(Pacs028ProtocolTests.Evidence evidence) : IClassFixture<Pacs028ProtocolTests.Evidence>
{
    private static readonly IpsReplyCorrelation Original = new("original-message", "original-transaction", "E2E-1");
    private const string Inquiry = "investigation-message";
    private static readonly InvestigationMessageContext Context = new(Inquiry, "request-identity", Created.AddMinutes(1));
    private static readonly XNamespace P = "urn:iso:std:iso:20022:tech:xsd:pacs.028.001.06";
    [Fact]
    public async Task Request_repeats_original_references_and_time_and_has_an_independently_verified_signature()
    {
        var payment = ValidatedPacs008.Validate(new Pacs008Request(Request()) { AcceptanceDateTime = Created.AddTicks(1234567) }, Policy).Payment!;
        var xml = new Pacs028Xml(new("NBGEGE22")).Build(payment, Original, Context);
        var document = XDocument.Parse(xml);
        Assert.Equal("original-message", document.Descendants(P + "OrgnlMsgId").Single().Value);
        Assert.Equal("original-transaction", document.Descendants(P + "OrgnlTxId").Single().Value);
        Assert.Equal("E2E-1", document.Descendants(P + "OrgnlEndToEndId").Single().Value);
        Assert.Equal("pacs.008.001.12", document.Descendants(P + "OrgnlMsgNmId").Single().Value);
        Assert.Equal("2026-10-03T23:59:59.1234567Z", document.Descendants(P + "AccptncDtTm").Single().Value);
        Assert.Equal(Inquiry, document.Descendants(P + "MsgId").Single().Value);
        Assert.Equal("request-identity", document.Descendants(P + "StsReqId").Single().Value);
        Assert.Equal("2026-10-04T00:00:59.0000000Z", document.Descendants(P + "CreDtTm").Single().Value);
        Assert.Equal(new[] { "BAGAGE22", "BAGAGE22", "TBCBGE22" }, document.Descendants(P + "BICFI").Select(e => e.Value));
        Assert.Equal("INST", document.Descendants(P + "SvcLvl").Single().Value);
        Assert.Equal("INST", document.Descendants(P + "LclInstrm").Single().Value);
        Assert.DoesNotContain("never-on-wire", xml);
        Assert.Equal(xml, new Pacs028Xml(new("NBGEGE22")).Build(payment, Original, Context));
        var signer = new Pacs008MessageSigner(new(false, false), TimeProvider.System);
        var signed = signer.PrepareInvestigation(xml, evidence.Ips);
        Assert.True(signed.IsSigned);
        Assert.True(await JavaSignatureVerifier.VerifyAsync(signed.Xml, evidence.Ips));
        Assert.Throws<InvalidOperationException>(() => signer.PrepareInvestigation(signed.Xml, evidence.Ips));
        Assert.Throws<SigningCertificateException>(() => signer.PrepareInvestigation(xml, null));
        var unsigned = new Pacs008MessageSigner(new(true, true), TimeProvider.System).PrepareInvestigation(xml, null);
        Assert.False(unsigned.IsSigned);
        Assert.Equal(xml, unsigned.Xml);
    }

    [Theory]
    [InlineData("payment-accepted", "ACCP", InvestigationOutcome.OriginalAccepted)]
    [InlineData("nested-payment-matching", "ACCP", InvestigationOutcome.OriginalAccepted)]
    [InlineData("nested-inquiry-matching", "RJCT/1016", InvestigationOutcome.NotFound)]
    [InlineData("payment-rejected", "RJCT/1009", InvestigationOutcome.OriginalRejected)]
    [InlineData("payment-rejected", "RJCT/1016", InvestigationOutcome.Unresolved)]
    [InlineData("payment-ambiguous", "RJCT", InvestigationOutcome.Unresolved)]
    [InlineData("inquiry-rejected", "RJCT/1016", InvestigationOutcome.NotFound)]
    [InlineData("inquiry-validation-rejected", "RJCT/1016", InvestigationOutcome.Unresolved)]
    [InlineData("inquiry-rejected", "RJCT/1017", InvestigationOutcome.Unresolved)]
    [InlineData("inquiry-rejected", "RJCT", InvestigationOutcome.Unresolved)]
    [InlineData("inquiry-rejected", "RJCT/9999", InvestigationOutcome.Unresolved)]
    [InlineData("inquiry-rejected", "ACCP/1016", InvestigationOutcome.Unresolved)]
    [InlineData("inquiry-rejected", null, InvestigationOutcome.Unresolved)]
    [InlineData("inquiry-accepted", "ACCP", InvestigationOutcome.Unresolved)]
    [InlineData("wrong-message", "RJCT/1016", InvestigationOutcome.Unresolved)]
    [InlineData("wrong-version", "RJCT/1016", InvestigationOutcome.Unresolved)]
    [InlineData("bad-prefix", "RJCT/1016", InvestigationOutcome.Unresolved)]
    [InlineData("bad-name", "RJCT/1016", InvestigationOutcome.Unresolved)]
    [InlineData("wrong-transaction", "ACCP", InvestigationOutcome.Unresolved)]
    [InlineData("wrong-end-to-end", "ACCP", InvestigationOutcome.Unresolved)]
    [InlineData("inquiry-transaction", "RJCT/1016", InvestigationOutcome.NotFound)]
    [InlineData("inquiry-wrong-transaction", "RJCT/1016", InvestigationOutcome.Unresolved)]
    [InlineData("untrusted", "RJCT/1016", InvestigationOutcome.Unresolved)]
    [InlineData("pending", "ACCP", InvestigationOutcome.Unresolved)]
    [InlineData("conflict", "ACCP", InvestigationOutcome.Unresolved)]
    public void Distinguishes_payment_outcomes_from_investigation_rejection(string fixture, string? status, InvestigationOutcome expected)
    {
        var result = Interpret(new(200, evidence.Signed[fixture], status is null ? [] : [new("X-MONTRAN-IPS-ReqSts", status)]));
        Assert.Equal(expected, result.Outcome);
        if (expected == InvestigationOutcome.NotFound)
        {
            Assert.Equal(1016, result.Details.IpsInternalCode);
        }

        if (expected == InvestigationOutcome.OriginalRejected)
        {
            Assert.Equal("AC01", result.Details.ReasonCode);
        }
    }

    [Theory]
    [InlineData("mixed-payment", "RJCT")]
    [InlineData("mixed-inquiry", "RJCT/1016")]
    [InlineData("nested-payment-id", "ACCP")]
    [InlineData("nested-payment-version", "ACCP")]
    [InlineData("nested-inquiry-id", "RJCT/1016")]
    [InlineData("nested-inquiry-version", "RJCT/1016")]
    public void All_reason_codes_and_nested_references_must_agree(string fixture, string header)
    {
        Assert.Equal(InvestigationOutcome.Unresolved,
            Interpret(new(200, evidence.Signed[fixture], [new("X-MONTRAN-IPS-ReqSts", header)])).Outcome);
    }

    [Fact]
    public void Investigation_diagnostic_does_not_describe_the_original_payment_as_rejected()
    {
        var result = Interpret(new(200, evidence.Signed["inquiry-rejected"], [new("X-MONTRAN-IPS-ReqSts", "RJCT/1016")]));
        Assert.Equal(InvestigationOutcome.NotFound, result.Outcome);
        Assert.Equal("IPS rejected the pacs.028.", result.Details.Description);
    }

    [Fact]
    public void Invalid_or_conflicting_transport_evidence_never_authorizes_resend()
    {
        var signed = evidence.Signed["inquiry-rejected"];
        foreach (var response in new IpsSubmissionResponse[]
        {
            new(500, signed, [new("X-MONTRAN-IPS-ReqSts", "RJCT/1016")]),
            new(200, signed, [new("X-MONTRAN-IPS-ReqSts", "RJCT/1016"), new("X-MONTRAN-IPS-ReqSts", "RJCT/1017")]),
            new(200, signed.Replace("AG09", "AC01"), [new("X-MONTRAN-IPS-ReqSts", "RJCT/1016")]),
            new(200, "<Message/>", [new("X-MONTRAN-IPS-ReqSts", "RJCT/1016")]),
            new(200, "<!DOCTYPE Message [<!ENTITY x 'y'>]><Message/>", []),
            new(200, "", [new("X-MONTRAN-IPS-ReqSts", "RJCT/1016")]),
            new(200, Unsigned(Evidence.InquiryRejected), [new("X-MONTRAN-IPS-ReqSts", "RJCT/1016")]),
        }

        )
        {
            Assert.Equal(InvestigationOutcome.Unresolved, Interpret(response).Outcome);
        }
    }

    private InvestigationReply Interpret(IpsSubmissionResponse response) => new Pacs028ReplyInterpreter([evidence.Ips]).Interpret(response, Original, Inquiry);
    public sealed class Evidence : IAsyncLifetime
    {
        public X509Certificate2 Ips { get; } = Certificate();
        public Dictionary<string, string> Signed { get; } = [];

        internal static readonly Reply Payment = new()
        {
            MessageId = Original.MessageId,
            TransactionId = Original.TransactionId,
            EndToEndId = Original.EndToEndId
        };
        internal static readonly Reply InquiryRejected = Payment with
        {
            MessageId = Inquiry,
            OriginalMessageName = "pacs.028.001.06",
            GroupStatus = "RJCT",
            IncludeTransaction = false,
            ReasonCode = "AG09"
        };
        public async Task InitializeAsync()
        {
            var fixtures = new Dictionary<string, Reply>
            {
                ["payment-accepted"] = Payment,
                ["payment-rejected"] = Payment with { GroupStatus = "RJCT", TransactionStatus = "RJCT", ReasonCode = "AC01" },
                ["payment-ambiguous"] = Payment with { GroupStatus = "RJCT", TransactionStatus = "RJCT", ReasonCode = "AG09" },
                ["inquiry-rejected"] = InquiryRejected,
                ["inquiry-validation-rejected"] = InquiryRejected with { ReasonCode = "FF01" },
                ["inquiry-accepted"] = InquiryRejected with { GroupStatus = "ACCP", ReasonCode = null },
                ["wrong-message"] = InquiryRejected with { MessageId = "another-inquiry" },
                ["wrong-version"] = InquiryRejected with { OriginalMessageName = "pacs.028.001.05" },
                ["bad-prefix"] = InquiryRejected with { OriginalMessageName = "pacs.0289" },
                ["bad-name"] = InquiryRejected with { OriginalMessageName = "pacs.028-invalid" },
                ["wrong-transaction"] = Payment with { TransactionId = "another-payment" },
                ["wrong-end-to-end"] = Payment with { EndToEndId = "another-payment" },
                ["inquiry-transaction"] = InquiryRejected with { IncludeTransaction = true, TransactionStatus = "RJCT" },
                ["inquiry-wrong-transaction"] = InquiryRejected with { IncludeTransaction = true, TransactionStatus = "RJCT", TransactionId = "another-payment" },
                ["pending"] = Payment with { GroupStatus = null, TransactionStatus = "PDNG" },
                ["conflict"] = Payment with { TransactionStatus = "RJCT" },
            };
            var signed = await SignAsync(Ips, fixtures.Values.Select(Unsigned).ToArray());
            foreach (var (name, xml) in fixtures.Keys.Zip(signed))
            {
                Signed[name] = xml;
            }

            var extra = new Dictionary<string, string>
            {
                ["mixed-payment"] = Unsigned(fixtures["payment-rejected"]).Replace("</pacs:StsRsnInf>", "</pacs:StsRsnInf><pacs:StsRsnInf><pacs:Rsn><pacs:Cd>AG09</pacs:Cd></pacs:Rsn></pacs:StsRsnInf>"),
                ["mixed-inquiry"] = Unsigned(InquiryRejected).Replace("</pacs:StsRsnInf>", "</pacs:StsRsnInf><pacs:StsRsnInf><pacs:Rsn><pacs:Cd>FF01</pacs:Cd></pacs:Rsn></pacs:StsRsnInf>"),
                ["nested-payment-matching"] = Nested(Payment, Original.MessageId, "pacs.008.001.12"),
                ["nested-inquiry-matching"] = Nested(fixtures["inquiry-transaction"], Inquiry, "pacs.028.001.06"),
                ["nested-payment-id"] = Nested(Payment, "another-message", "pacs.008.001.12"),
                ["nested-payment-version"] = Nested(Payment, Original.MessageId, "pacs.008.001.11"),
                ["nested-inquiry-id"] = Nested(fixtures["inquiry-transaction"], "another-message", "pacs.028.001.06"),
                ["nested-inquiry-version"] = Nested(fixtures["inquiry-transaction"], Inquiry, "pacs.028.001.05"),
            };
            var additional = await SignAsync(Ips, extra.Values.ToArray());
            foreach (var (name, xml) in extra.Keys.Zip(additional))
            {
                Signed[name] = xml;
            }

            using var untrusted = Certificate("CN=Untrusted");
            Signed["untrusted"] = (await SignAsync(untrusted, Unsigned(InquiryRejected)))[0];
        }

        private static string Nested(Reply reply, string messageId, string definition) => Unsigned(reply).Replace("</pacs:StsId>", $"</pacs:StsId><pacs:OrgnlGrpInf><pacs:OrgnlMsgId>{messageId}</pacs:OrgnlMsgId><pacs:OrgnlMsgNmId>{definition}</pacs:OrgnlMsgNmId></pacs:OrgnlGrpInf>");
        public Task DisposeAsync()
        {
            Ips.Dispose();
            return Task.CompletedTask;
        }
    }
}
