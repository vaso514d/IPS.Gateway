using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Inbound.StatusReports;
using IPS.Middleware.Infrastructure.Payments.Investigation;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using Xunit;
using static IPS.Middleware.IntegrationTests.Payments.IpsReplies;

namespace IPS.Middleware.IntegrationTests.Payments;

public sealed class IpsReplyInterpreterTests(IpsReplyInterpreterTests.SignedReplies replies) : IClassFixture<IpsReplyInterpreterTests.SignedReplies>
{
    private static readonly IpsReplyCorrelation Sent = new("0123456789abcdef0123456789abcdef", "fedcba9876543210fedcba9876543210", "E2E-1");
    private static readonly Reply Accepted = new() { MessageId = Sent.MessageId, TransactionId = Sent.TransactionId, EndToEndId = Sent.EndToEndId };

    public static readonly TheoryData<string, string?, IpsReplyStatus, string?> Statuses = new()
    {
        { "accepted", "ACCP", IpsReplyStatus.Accepted, null },
        { "settled", null, IpsReplyStatus.Accepted, null },
        { "group-only", "ACCP", IpsReplyStatus.Accepted, null },
        { "rejected", "RJCT/1009", IpsReplyStatus.Rejected, null },
        { "pending", "ACCP", IpsReplyStatus.Unresolved, "did not return a final" },
        { "contradictory", null, IpsReplyStatus.Unresolved, "conflicting statuses" },
        { "accepted", "RJCT/1009", IpsReplyStatus.Unresolved, "conflicting statuses" },
        { "wrong-message", "ACCP", IpsReplyStatus.Unresolved, "does not reference this payment" },
        { "wrong-transaction", "ACCP", IpsReplyStatus.Unresolved, "does not reference this payment" },
        { "wrong-end-to-end", "ACCP", IpsReplyStatus.Unresolved, "does not reference this payment" },
        { "wrong-original-type", "ACCP", IpsReplyStatus.Unresolved, "does not reference this payment" },
        { "invalid-original-prefix", "ACCP", IpsReplyStatus.Unresolved, "does not reference this payment" },
        { "invalid-original-suffix", "ACCP", IpsReplyStatus.Unresolved, "does not reference this payment" },
        { "unsupported-original-version", "ACCP", IpsReplyStatus.Unresolved, "does not reference this payment" },
        { "untrusted", "ACCP", IpsReplyStatus.Unresolved, "signature" },
    };

    [Theory]
    [MemberData(nameof(Statuses))]
    public void Only_signed_correlated_agreeing_final_statuses_are_final(
        string fixture, string? requestStatus, IpsReplyStatus expected, string? reason)
    {
        var reply = Interpret(replies.Signed[fixture], requestStatus);
        Assert.True(expected == reply.Status, $"{reply.Status}: {reply.Details.Description}");
        if (reason is not null)
        {
            Assert.Contains(reason, reply.Details.Description);
        }
    }

    [Fact]
    public void Valid_rejection_preserves_source_reason_code_ips_code_and_description()
    {
        var reply = Interpret(replies.Signed["rejected"], "RJCT/1009");
        Assert.Equal(IpsReplyStatus.Rejected, reply.Status);
        Assert.Equal("AC01", reply.Details.ReasonCode);
        Assert.Equal(1009, reply.Details.IpsInternalCode);
        Assert.Equal("The creditor IBAN code is invalid.", reply.Details.Description);
        var accepted = Interpret(replies.Signed["accepted"], "ACCP");
        Assert.Equal("IPS accepted the pacs.008.", accepted.Details.Description);
        Assert.Null(accepted.Details.ReasonCode);
    }

    [Fact]
    public void Tampered_unsigned_malformed_schema_invalid_and_non_200_replies_are_unresolved()
    {
        var signed = replies.Signed["accepted"];
        Assert.Equal(IpsReplyStatus.Accepted, Interpret(signed, "ACCP").Status);
        foreach (var body in new[]
        {
            signed.Replace("<pacs:TxSts>ACCP</pacs:TxSts>", "<pacs:TxSts>ACSC</pacs:TxSts>"),
            Unsigned(Accepted),
            signed[..^20],
            signed.Replace("<pacs:StsId>IPS-STS-1</pacs:StsId>", ""),
            "<!DOCTYPE Message [<!ENTITY x \"y\">]><Message/>",
            PresentTrustedCertificate(replies.Signed["untrusted"]),
            signed.Replace("xmldsig-more#ecdsa-sha256", "xmldsig-more#rsa-sha256"),
            signed.Replace("URI=\"\"", "URI=\"#other\""),
        })
        {
            Assert.Equal(IpsReplyStatus.Unresolved, Interpret(body, "ACCP").Status);
        }

        foreach (var response in new IpsSubmissionResponse[]
        {
            new(500, signed, [new("X-MONTRAN-IPS-ReqSts", "ACCP")]),
            new(200, "", [new("X-MONTRAN-IPS-ReqSts", "ACCP")]),
            new(200, signed, [new("X-MONTRAN-IPS-ReqSts", "ACCP"), new("X-MONTRAN-IPS-ReqSts", "RJCT/1009")]),
        })
        {
            Assert.Equal(IpsReplyStatus.Unresolved, Interpreter(replies.Ips).Interpret(response, Sent).Status);
        }
    }

    // A reply and an investigation answer are judged when they are interpreted; an unsolicited report as of its receipt,
    // however late it is processed. Each is refused when the signing certificate was not valid at that moment.
    [Theory]
    [MemberData(nameof(SignatureValidity.Moments), MemberType = typeof(SignatureValidity))]
    public void Replies_and_reports_are_trusted_only_while_the_signing_certificate_is_valid(string moment, bool valid)
    {
        var at = SignatureValidity.At(replies.Ips, moment);
        var trust = new IpsSignatureTrust([replies.Ips], new TestClock(at));
        var processedLate = new IpsSignatureTrust([replies.Ips], new TestClock(SignatureValidity.LongAfterExpiry(replies.Ips)));
        var response = new IpsSubmissionResponse(200, replies.Signed["accepted"], [new("X-MONTRAN-IPS-ReqSts", "ACCP")]);

        var reply = new IpsReplyInterpreter(trust).Interpret(response, Sent);
        var investigation = new Pacs028ReplyInterpreter(trust).Interpret(response, Sent, "INQUIRY-1");
        var report = new StatusReportProtocol(processedLate).Interpret(replies.Signed["accepted"], Sent, receivedAtUtc: at);

        if (valid)
        {
            Assert.Equal(IpsReplyStatus.Accepted, reply.Status);
            Assert.Equal(InvestigationOutcome.OriginalAccepted, investigation.Outcome);
            Assert.Equal(IpsReplyStatus.Accepted, report.Status);
        }
        else
        {
            var reason = SignatureValidity.ReplyUnresolved(replies.Ips);
            Assert.Equal((IpsReplyStatus.Unresolved, IpsReplyStatus.Unresolved), (reply.Status, report.Status));
            Assert.Equal(InvestigationOutcome.Unresolved, investigation.Outcome);
            Assert.Equal(reason, reply.Details.Description);
            Assert.Equal(reason, investigation.Details.Description);
            Assert.Equal(reason, report.Details.Description);
        }
    }

    // Annex C 2.1: the next IPS certificate is configured before IPS switches to it; each verifies only within its period.
    [Fact]
    public async Task Rotation_hands_trust_from_the_expiring_certificate_to_the_next_at_their_bounds()
    {
        var expiry = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var start = expiry.AddDays(-7);
        using var expiring = Certificate("CN=Expiring IPS", expiry.AddYears(-1), expiry);
        using var next = Certificate("CN=Next IPS", start, expiry.AddYears(1));
        var signedByExpiring = (await SignAsync(expiring, Unsigned(Accepted)))[0];
        var signedByNext = (await SignAsync(next, Unsigned(Accepted)))[0];
        var clock = new TestClock(start);
        var interpreter = new IpsReplyInterpreter(new IpsSignatureTrust([expiring, next], clock));
        IpsReply ReplyAt(DateTimeOffset now, string body)
        {
            clock.Now = now;
            return interpreter.Interpret(new(200, body, [new("X-MONTRAN-IPS-ReqSts", "ACCP")]), Sent);
        }

        Assert.Equal(IpsReplyStatus.Accepted, ReplyAt(expiry, signedByExpiring).Status);
        var expired = ReplyAt(expiry.AddTicks(1), signedByExpiring);
        Assert.Equal((IpsReplyStatus.Unresolved, SignatureValidity.ReplyUnresolved(expiring)), (expired.Status, expired.Details.Description));
        var early = ReplyAt(start.AddTicks(-1), signedByNext);
        Assert.Equal((IpsReplyStatus.Unresolved, SignatureValidity.ReplyUnresolved(next)), (early.Status, early.Details.Description));
        Assert.Equal(IpsReplyStatus.Accepted, ReplyAt(start, signedByNext).Status);
    }

    private IpsReply Interpret(string body, string? requestStatus) =>
        Interpreter(replies.Ips).Interpret(new(200, body, requestStatus is null ? [] : [new("X-MONTRAN-IPS-ReqSts", requestStatus)]), Sent);

    // Present the trusted certificate for a signature made with another key.
    private string PresentTrustedCertificate(string signed)
    {
        var document = XDocument.Parse(signed, LoadOptions.PreserveWhitespace);
        document.Descendants(XName.Get("X509Certificate", "http://www.w3.org/2000/09/xmldsig#")).Single().Value =
            Convert.ToBase64String(replies.Ips.RawData);
        var presented = document.ToString(SaveOptions.DisableFormatting);
        Assert.Contains(Convert.ToBase64String(replies.Ips.RawData), presented);
        return presented;
    }

    private static IpsReplyInterpreter Interpreter(X509Certificate2 trusted) => new(new IpsSignatureTrust([trusted], TimeProvider.System));

    public sealed class SignedReplies : IAsyncLifetime
    {
        public X509Certificate2 Ips { get; } = Certificate();
        public X509Certificate2 Untrusted { get; } = Certificate("CN=Untrusted");
        public Dictionary<string, string> Signed { get; } = [];

        public async Task InitializeAsync()
        {
            var fixtures = new Dictionary<string, Reply>
            {
                ["accepted"] = Accepted,
                ["settled"] = Accepted with { GroupStatus = null, TransactionStatus = "ACSC" },
                ["group-only"] = Accepted with { IncludeTransaction = false },
                ["rejected"] = Accepted with { GroupStatus = "RJCT", TransactionStatus = "RJCT", ReasonCode = "AC01", AdditionalInformation = "The creditor IBAN code is invalid." },
                ["pending"] = Accepted with { GroupStatus = null, TransactionStatus = "PDNG" },
                ["contradictory"] = Accepted with { TransactionStatus = "RJCT", ReasonCode = "AC01" },
                ["wrong-message"] = Accepted with { MessageId = "00000000000000000000000000000000" },
                ["wrong-transaction"] = Accepted with { TransactionId = "00000000000000000000000000000000" },
                ["wrong-end-to-end"] = Accepted with { EndToEndId = "E2E-2" },
                ["wrong-original-type"] = Accepted with { OriginalMessageName = "pacs.028.001.06" },
                ["invalid-original-prefix"] = Accepted with { OriginalMessageName = "pacs.0089" },
                ["invalid-original-suffix"] = Accepted with { OriginalMessageName = "pacs.008-invalid" },
                ["unsupported-original-version"] = Accepted with { OriginalMessageName = "pacs.008.001.11" },
            };
            var signed = await SignAsync(Ips, fixtures.Values.Select(Unsigned).ToArray());
            foreach (var (name, xml) in fixtures.Keys.Zip(signed))
            {
                Signed[name] = xml;
            }

            Signed["untrusted"] = (await SignAsync(Untrusted, Unsigned(Accepted)))[0];
        }

        public Task DisposeAsync()
        {
            Ips.Dispose();
            Untrusted.Dispose();
            return Task.CompletedTask;
        }
    }
}
