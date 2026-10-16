using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.IntegrationTests.Payments;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

public sealed class IncomingPacs008ProtocolTests(IncomingPacs008Fixture fixture) : IClassFixture<IncomingPacs008Fixture>
{
    [Fact]
    public void Trusted_fixture_maps_to_independently_expected_core_json_and_original_references()
    {
        var parsed = Ready();
        var actual = JsonSerializer.SerializeToNode(IncomingPacs008CoreMapping.ToContract(parsed));
        var expected = JsonNode.Parse("""
            {"clientReference":null,"instructionId":"INSTRUCTION-1","endToEndId":"E2E-1",
            "creationDateTime":"2026-10-04T12:00:00+00:00","acceptanceDateTime":"2026-10-04T12:00:00+00:00",
            "amount":12.50,"currency":"GEL","instructionPriority":"HIGH","categoryPurposeCode":"SUPP",
            "debtor":{"participantBic":"ITSBGE22","type":0,"name":"Payer & Co","identifier":"ORG-1","billIdentifier":"BILL-1",
              "address":{"streetName":"Main","buildingNumber":"1","postCode":"0100","townName":"Tbilisi","countrySubdivision":"Region","country":"GE","addressLines":"Part 1Part 2"},
              "indirectParticipantBic":"SENDER-MEMBER","account":"GE29NB0000000101904917"},
            "creditor":{"type":1,"name":"Payee","identifier":"PERSON-2","address":null,"participantBic":"BAGAGE22","indirectParticipantBic":"RECEIVER-MEMBER","account":"TREASURY-1"},
            "ultimateDebtor":{"type":0,"name":"Ultimate payer","identifier":"12345678901234567890"},
            "ultimateCreditor":{"type":0,"name":"Ultimate payee","identifier":"TBCBGE22"},
            "paymentInitiation":{"channelCode":"QR","geolocation":["41.7","44.8"]},
            "initiationChannelInstrument":{"channelCode":"MOBL","instrumentCodes":["PRXY","BILL"],"electronicAddress":"device:42"},
            "remittance":{"unstructured":"Invoice 42","structured":[
              {"referenceType":"MCC","referenceIssuer":"Issuer","reference":"5411","additionalInformation":"Part APart B"},
              {"referenceType":"SERV","referenceIssuer":null,"reference":"ORDER-1","additionalInformation":null}]}}
            """);
        Assert.True(JsonNode.DeepEquals(expected, actual), actual!.ToJsonString());
        Assert.Equal("IN-HEADER-1", parsed.Original.BusinessMessageId);
        Assert.Equal("IN-GROUP-1", parsed.Original.GroupMessageId);
        Assert.Equal("TX-1", parsed.Original.TransactionId);
        Assert.Equal(Guid.Parse("12345678-1234-4234-8234-123456789012"), parsed.Original.Uetr);
        Assert.Equal(new DateOnly(2026, 10, 4), parsed.Original.SettlementDate);
    }

    [Theory]
    [InlineData("batch")]
    [InlineData("count")]
    [InlineData("count-supplement")]
    [InlineData("missing-count")]
    [InlineData("invalid-count")]
    public void Trusted_count_violations_are_correlated_FF01_rejections(string name)
    {
        var result = Assert.IsType<IncomingPacs008ReadResult.Reject>(Read(fixture.Signed[name]));
        Assert.Equal("FF01", result.ReasonCode);
        Assert.Equal("IN-GROUP-1", result.Original.GroupMessageId);
    }

    [Theory]
    [InlineData("count-wrong-order")]
    [InlineData("batch-wrong-second-order")]
    [InlineData("missing-date-and-count")]
    [InlineData("missing-id")]
    [InlineData("bad-amount")]
    [InlineData("wrong-version")]
    [InlineData("wrong-name")]
    [InlineData("wrong-namespace")]
    [InlineData("extra-header")]
    [InlineData("batch-bad-second")]
    public void Signed_but_invalid_inputs_are_held_without_a_reply_decision(string name) => Assert.IsType<IncomingPacs008ReadResult.Hold>(Read(fixture.Signed[name]));
    [Fact]
    public void Unsigned_untrusted_tampered_and_unsafe_xml_are_held()
    {
        Assert.IsType<IncomingPacs008ReadResult.Hold>(Read(IncomingPacs008Fixture.Xml));
        Assert.IsType<IncomingPacs008ReadResult.Hold>(Read(fixture.Signed["valid"].Replace("12.50", "13.50", StringComparison.Ordinal)));
        // 012d: without a configured IPS certificate nothing is verified.
        Assert.IsType<IncomingPacs008ReadResult.Ready>(new IncomingPacs008Reader().Read(fixture.Signed["valid"], new IpsSignatureTrust([], TimeProvider.System), DateTimeOffset.UtcNow));
        Assert.IsType<IncomingPacs008ReadResult.Hold>(Read("<!DOCTYPE Message [<!ENTITY x SYSTEM 'file:///unread'>]><Message>&x;</Message>"));
        Assert.IsType<IncomingPacs008ReadResult.Hold>(Read("<broken"));
    }

    [Theory]
    [MemberData(nameof(SignatureValidity.Moments), MemberType = typeof(SignatureValidity))]
    public void A_payment_is_trusted_only_if_its_signing_certificate_was_valid_when_it_was_received(string moment, bool valid)
    {
        // Processing happens long after the certificate expired; only the receipt time counts.
        var processing = new TestClock(SignatureValidity.LongAfterExpiry(fixture.Certificate));
        var trust = new IpsSignatureTrust([fixture.Certificate], processing);

        var result = new IncomingPacs008Reader().Read(fixture.Signed["valid"], trust, SignatureValidity.At(fixture.Certificate, moment));

        if (valid)
        {
            Assert.IsType<IncomingPacs008ReadResult.Ready>(result);
        }
        else
        {
            Assert.Equal(SignatureValidity.IncomingHold(fixture.Certificate), Assert.IsType<IncomingPacs008ReadResult.Hold>(result).Reason);
        }
    }

    [Fact]
    public void Nested_collections_are_read_only_and_contract_mapping_cannot_mutate_the_snapshot()
    {
        var incoming = Ready();
        var payment = incoming.Payment;
        Assert.Throws<NotSupportedException>(() => ((IList<string>)payment.PaymentInitiation!.Geolocation!)[0] = "changed");
        Assert.Throws<NotSupportedException>(() => ((IList<string>)payment.InitiationChannelInstrument!.InstrumentCodes!)[0] = "changed");
        Assert.Throws<NotSupportedException>(() => ((IList<Pacs008StructuredRemittanceInput>)payment.Remittance!.Structured!)[0] = new());
        var dto = IncomingPacs008CoreMapping.ToContract(incoming);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)dto.PaymentInitiation!.Geolocation!)[0] = "changed");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reply_is_correlated_deterministic_schema_valid_and_independently_signed(bool accepted)
    {
        var original = Ready().Original;
        var time = new DateTimeOffset(2026, 10, 4, 12, 0, 3, TimeSpan.Zero);
        var context = new IncomingReplyContext("REPLY-ID", "STATUS-ID", time);
        var decision = new IncomingReplyDecision(accepted, time, "AC01", "Invalid account");
        var builder = new IncomingPacs002Reply(new("NBGEGE22"), new(new(false, true), TimeProvider.System));
        var unsigned = builder.BuildUnsigned(original, decision, context, "BAGAGE22");
        Assert.Equal(unsigned, builder.BuildUnsigned(original, decision, context, "BAGAGE22"));
        var signed = builder.Prepare(original, decision, context, "BAGAGE22", fixture.Certificate);
        Assert.True(signed.IsSigned);
        Assert.True(SignatureVerifier.Verifies(signed.Xml, fixture.Certificate));
        XNamespace pacs = "urn:iso:std:iso:20022:tech:xsd:pacs.002.001.14";
        var doc = XDocument.Parse(signed.Xml);
        Assert.Equal("IN-GROUP-1", doc.Descendants(pacs + "OrgnlMsgId").Single().Value);
        Assert.Equal("pacs.008.001.12", doc.Descendants(pacs + "OrgnlMsgNmId").Single().Value);
        Assert.Equal("E2E-1", doc.Descendants(pacs + "OrgnlEndToEndId").Single().Value);
        Assert.Equal("TX-1", doc.Descendants(pacs + "OrgnlTxId").Single().Value);
        Assert.Equal(accepted ? "ACCP" : "RJCT", doc.Descendants(pacs + "TxSts").Single().Value);
        Assert.Equal(accepted ? 0 : 2, doc.Descendants(pacs + "StsRsnInf").Count());
        Assert.Empty(doc.Descendants(pacs + "OrgnlUETR"));
        Assert.Empty(doc.Descendants(pacs + "OrgnlInstrId"));
        Assert.Throws<SigningCertificateException>(() => builder.Prepare(original, decision, context, "BAGAGE22", null));
        var development = new IncomingPacs002Reply(new("NBGEGE22"), new(new(true, true), TimeProvider.System));
        Assert.False(development.Prepare(original, decision, context, "BAGAGE22", null).IsSigned);
    }

    [Fact]
    public void Optional_fields_stay_absent_and_reply_fallbacks_preserve_the_source_profile()
    {
        var incoming = Assert.IsType<IncomingPacs008ReadResult.Ready>(Read(fixture.Signed["minimal"])).Payment;
        var payment = incoming.Payment;
        Assert.Null(payment.InstructionId);
        Assert.Null(payment.AcceptanceDateTime);
        Assert.Null(payment.InstructionPriority);
        Assert.Null(payment.CategoryPurposeCode);
        Assert.Null(payment.PaymentInitiation);
        Assert.Null(payment.InitiationChannelInstrument);
        Assert.Null(payment.Remittance);
        Assert.Null(payment.UltimateDebtor);
        Assert.Null(payment.UltimateCreditor);
        Assert.Null(payment.Debtor!.Address);
        Assert.Null(payment.Debtor.IndirectParticipantBic);
        var time = new DateTimeOffset(2026, 10, 4, 12, 0, 3, TimeSpan.Zero).AddTicks(1234567);
        var context = new IncomingReplyContext("fixed-reply", "fixed-status", time);
        var builder = new IncomingPacs002Reply(new("NBGEGE22"), new(new(false, true), TimeProvider.System));
        var xml = builder.BuildUnsigned(incoming.Original, new(false, default, Description: "  " + new string('x', 50) + "  "), context, "BAGAGE22");
        var doc = XDocument.Parse(xml);
        XNamespace p = "urn:iso:std:iso:20022:tech:xsd:pacs.002.001.14";
        Assert.Equal("2026-10-04T12:00:03.1234567Z", doc.Descendants(p + "AccptncDtTm").Single().Value);
        Assert.Equal("INST", doc.Descendants(p + "SvcLvl").Single().Element(p + "Cd")!.Value);
        Assert.Equal("INST", doc.Descendants(p + "LclInstrm").Single().Element(p + "Cd")!.Value);
        Assert.All(doc.Descendants(p + "Rsn"), reason => Assert.Equal("MS03", reason.Element(p + "Cd")!.Value));
        Assert.All(doc.Descendants(p + "AddtlInf"), info => Assert.Equal(new string('x', 35), info.Value));
    }

    private IncomingPacs008ReadResult Read(string xml) => new IncomingPacs008Reader().Read(xml, new IpsSignatureTrust([fixture.Certificate], TimeProvider.System), DateTimeOffset.UtcNow);
    private IncomingPacs008 Ready() => Assert.IsType<IncomingPacs008ReadResult.Ready>(Read(fixture.Signed["valid"])).Payment;
}
