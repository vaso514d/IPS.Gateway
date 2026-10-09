using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using Xunit;
using static IPS.Middleware.IntegrationTests.Payments.Pacs008Fixture;

namespace IPS.Middleware.IntegrationTests.Payments;

public sealed class Pacs008XmlTests
{
    private static readonly XNamespace P = Pacs008Xml.DocumentNamespace;
    private static readonly XNamespace H = Pacs008Xml.HeaderNamespace;
    private static readonly PaymentMessageContext Context = new("stored-message", "stored-transaction", Created.AddSeconds(3));
    [Fact]
    public void Minimal_message_has_expected_identifiers_business_times_amounts_and_order()
    {
        var xml = Build(Request());
        Assert.DoesNotContain("never-on-wire", xml, StringComparison.Ordinal);
        var document = XDocument.Parse(xml);
        Assert.Equal("stored-message", document.Descendants(H + "BizMsgIdr").Single().Value);
        Assert.Equal("stored-message", document.Descendants(P + "MsgId").Single().Value);
        Assert.Equal("stored-transaction", document.Descendants(P + "TxId").Single().Value);
        Assert.Equal("2026-10-04", document.Descendants(P + "IntrBkSttlmDt").Single().Value);
        Assert.Equal("2026-10-03T23:59:59.5000000Z", document.Descendants(P + "AccptncDtTm").Single().Value);
        Assert.Equal("12.34567", document.Descendants(P + "IntrBkSttlmAmt").Single().Value);
        Assert.Equal("GEL", document.Descendants(P + "IntrBkSttlmAmt").Single().Attribute("Ccy")!.Value);
        Assert.Equal(new[] { "PmtId", "IntrBkSttlmAmt", "AccptncDtTm", "ChrgBr", "Dbtr", "DbtrAcct", "DbtrAgt", "CdtrAgt", "Cdtr", "CdtrAcct" },
            document.Descendants(P + "CdtTrfTxInf").Single().Elements().Select(e => e.Name.LocalName));
        Assert.Equal("SLEV", document.Descendants(P + "ChrgBr").Single().Value);
        Assert.Equal("ქართული & Debtor", document.Descendants(P + "Dbtr").Single().Element(P + "Nm")!.Value);
        Assert.Empty(document.Descendants(P + "UETR"));
        Assert.Empty(document.Descendants(P + "Purp"));
        Assert.Empty(document.Descendants(P + "RmtInf"));
    }

    [Fact]
    public void Full_profile_splits_text_without_loss_and_maps_optional_blocks()
    {
        var r = Request();
        var input = r with
        {
            CategoryPurposeCode = "othr",
            Debtor = r.Debtor! with
            {
                Identifier = "ordinary",
                BillIdentifier = "bill",
                IndirectParticipantBic = "MEMBER-1",
                Address = new()
                {
                    StreetName = "Street",
                    BuildingNumber = "1",
                    TownName = "City",
                    PostCode = "0100",
                    Country = "GE",
                    CountrySubdivision = "Region",
                    AddressLines = new string('a', 490)
                }
            },
            Creditor = r.Creditor! with
            {
                Type = 0,
                Identifier = "400000002",
                IndirectParticipantBic = "OTHER-MEMBER"
            },
            UltimateDebtor = new()
            {
                Type = 1,
                Name = "Ultimate debtor",
                Identifier = "ID-1"
            },
            UltimateCreditor = new()
            {
                Type = 0,
                Name = "Ultimate creditor"
            },
            PaymentInitiation = new()
            {
                ChannelCode = "MB",
                Geolocation = ["41.7,44.8", "41.8,44.9"]
            },
            InitiationChannelInstrument = new()
            {
                ChannelCode = "MOBL",
                InstrumentCodes = ["CARD", "PRXY"],
                ElectronicAddress = "41.7,44.8"
            },
            Remittance = new()
            {
                Unstructured = new string('u', 421),
                Structured = [new()
                {
                    ReferenceType = "SERV",
                    Reference = "order",
                    ReferenceIssuer = "issuer",
                    AdditionalInformation = new string ('i', 420)
                }

                ]
            }
        };
        var doc = XDocument.Parse(Build(input));
        Assert.Equal(7, doc.Descendants(P + "AdrLine").Count());
        Assert.Equal(new string('a', 490), string.Concat(doc.Descendants(P + "AdrLine").Select(e => e.Value)));
        Assert.Equal(4, doc.Descendants(P + "Ustrd").Count());
        Assert.Equal(new string('u', 421), string.Concat(doc.Descendants(P + "Ustrd").Select(e => e.Value)));
        Assert.Equal(3, doc.Descendants(P + "AddtlRmtInf").Count());
        Assert.Equal(new[] { "MOBL:CARD", "MOBL:PRXY" }, doc.Descendants(P + "RmtId").Select(e => e.Value));
        Assert.Equal(new[] { "URID", "URID" }, doc.Descendants(P + "Mtd").Select(e => e.Value));
        Assert.Equal("BILL", doc.Descendants(P + "SchmeNm").Single().Value);
        Assert.Equal(2, doc.Descendants(P + "Dbtr").Single().Descendants(P + "Othr").Count());
        Assert.Equal("OTHR", doc.Descendants(P + "CtgyPurp").Single().Value);
        Assert.All(doc.Descendants(P + "ClrSysId"), e => Assert.Equal("GE", e.Value));
    }

    [Fact]
    public void Treasury_and_initiation_preserve_their_special_mapping()
    {
        var request = Request();
        var doc = XDocument.Parse(Build(request with { EndToEndId = "PSP-original", AcceptanceDateTime = Created.AddDays(-1), Creditor = request.Creditor! with { ParticipantBic = "TRESGE22", Account = "300773150" } }));
        var account = doc.Descendants(P + "CdtrAcct").Single();
        Assert.Empty(account.Descendants(P + "IBAN"));
        Assert.Equal("300773150", account.Element(P + "Id")!.Element(P + "Othr")!.Element(P + "Id")!.Value);
        Assert.Equal("PSP-original", doc.Descendants(P + "EndToEndId").Single().Value);
    }

    [Fact]
    public void Schema_validation_rejects_bad_order_namespaces_and_external_entities()
    {
        var valid = XDocument.Parse(Build(Request()));
        var transaction = valid.Descendants(P + "CdtTrfTxInf").Single();
        var amount = transaction.Element(P + "IntrBkSttlmAmt")!;
        amount.Remove();
        transaction.Add(amount);
        Assert.Throws<XmlSchemaValidationException>(() => Pacs008Schema.Validate(valid.ToString()));
        Assert.Throws<XmlSchemaValidationException>(() => Pacs008Schema.Validate("<Message><AppHdr/><Document/></Message>"));
        Assert.Throws<XmlException>(() => Pacs008Schema.Validate("<!DOCTYPE Message [<!ENTITY external SYSTEM 'file:///nonexistent'>]><Message>&external;</Message>"));
    }

    [Fact]
    public void Configured_protocol_codes_and_source_whitespace_normalization_are_preserved()
    {
        var request = Request();
        var input = request with
        {
            Debtor = request.Debtor! with
            {
                Address = new()
                {
                    AddressLines = new string('a', 69) + " " + "b" + new string(' ', 70),
                    StreetName = "  street  "
                }
            },
            PaymentInitiation = new()
            {
                ChannelCode = "MB",
                Geolocation = [" 41.7,44.8 "]
            },
            InitiationChannelInstrument = new()
            {
                ChannelCode = "MOBL",
                InstrumentCodes = ["CARD"],
                ElectronicAddress = "41.7,44.8"
            },
            Remittance = new()
            {
                Structured = [new()
                {
                    ReferenceType = "SERV",
                    Reference = "ref",
                    ReferenceIssuer = " issuer "
                }

                ]
            }
        };
        var result = ValidatedPacs008.Validate(input, Policy);
        Assert.Empty(result.Errors);
        var doc = XDocument.Parse(new Pacs008Xml(new("NBGEGE22", "SEPA", RemittanceDeliveryMethod.Email)).Build(result.Payment!, Context));
        Assert.Equal(new[] { new string('a', 69), "b" }, doc.Descendants(P + "AdrLine").Select(e => e.Value));
        Assert.Equal("street", doc.Descendants(P + "StrtNm").Single().Value);
        Assert.Equal("issuer", doc.Descendants(P + "Issr").Single().Value);
        Assert.Equal("41.7,44.8", doc.Descendants(P + "Inf").Single().Value);
        Assert.Equal("EMAL", doc.Descendants(P + "Mtd").Single().Value);
        Assert.Equal("SEPA", doc.Descendants(P + "SvcLvl").Single().Value);
    }

    [Theory]
    [InlineData(RemittanceDeliveryMethod.Fax, "FAXI")]
    [InlineData(RemittanceDeliveryMethod.ElectronicDataInterchange, "EDIC")]
    [InlineData(RemittanceDeliveryMethod.Uri, "URID")]
    [InlineData(RemittanceDeliveryMethod.Email, "EMAL")]
    [InlineData(RemittanceDeliveryMethod.Post, "POST")]
    [InlineData(RemittanceDeliveryMethod.Sms, "SMSM")]
    public void Typed_remittance_methods_keep_the_protocol_codes(RemittanceDeliveryMethod method, string expected)
    {
        var result = ValidatedPacs008.Validate(Request() with
        {
            InitiationChannelInstrument = new() { ChannelCode = "MOBL", InstrumentCodes = ["CARD"], ElectronicAddress = "address" }
        }, Policy);
        Assert.Empty(result.Errors);
        var xml = new Pacs008Xml(new("NBGEGE22", remittanceMethod: method)).Build(result.Payment!, Context);
        Assert.Equal(expected, XDocument.Parse(xml).Descendants(P + "Mtd").Single().Value);
    }

    [Fact]
    public void Configuration_is_validated_before_building_a_message()
    {
        Assert.Throws<ArgumentException>(() => new Pacs008ProtocolProfile("bad-bic"));
        Assert.Throws<ArgumentException>(() => new Pacs008ProtocolProfile("NBGEGE22", "TOOLONG"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pacs008ProtocolProfile("NBGEGE22", remittanceMethod: (RemittanceDeliveryMethod)100));
        Assert.Throws<ArgumentException>(() => new PaymentMessageContext(" ", "transaction", Created));
    }

    [Fact]
    public void Empty_optional_blocks_are_omitted_and_numeric_lexemes_ignore_current_culture()
    {
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("fr-FR");
            var request = Request();
            var xml = Build(request with { Amount = 12.34000m, Debtor = request.Debtor! with { Address = new() { StreetName = " ", AddressLines = "  " } }, PaymentInitiation = new(), Remittance = new() { Unstructured = " " } });
            var document = XDocument.Parse(xml);
            Assert.Equal("12.34", document.Descendants(P + "IntrBkSttlmAmt").Single().Value);
            Assert.Equal("2026-10-04T00:00:02.0000000Z", document.Descendants(H + "CreDt").Single().Value);
            Assert.Equal("CLRG", document.Descendants(P + "SttlmMtd").Single().Value);
            Assert.Equal("IPS", document.Descendants(P + "ClrSys").Single().Value);
            Assert.Empty(document.Descendants(P + "PstlAdr"));
            Assert.Empty(document.Descendants(P + "RgltryRptg"));
            Assert.Empty(document.Descendants(P + "RmtInf"));
            Assert.DoesNotContain("xsi:nil", xml, StringComparison.Ordinal);
            Assert.DoesNotContain("<?xml", xml, StringComparison.Ordinal);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public async Task Reused_builder_keeps_parallel_message_contexts_separate()
    {
        var builder = new Pacs008Xml(new("NBGEGE22"));
        var payment = ValidatedPacs008.Validate(Request(), Policy).Payment!;
        var messages = await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(() =>
            builder.Build(payment, new("message-" + index, "transaction-" + index, Created)))));
        for (var index = 0; index < messages.Length; index++)
        {
            var document = XDocument.Parse(messages[index]);
            Assert.Equal("message-" + index, document.Descendants(H + "BizMsgIdr").Single().Value);
            Assert.Equal("transaction-" + index, document.Descendants(P + "TxId").Single().Value);
        }
    }

    [Fact]
    public void Accepted_currency_whitespace_is_normalized_before_schema_validation()
    {
        var input = Request() with
        {
            Currency = "GEL\n"
        };
        var result = ValidatedPacs008.Validate(input, Policy);
        Assert.Empty(result.Errors);
        Assert.Equal("GEL", result.Payment!.Currency);
        var document = XDocument.Parse(Build(input));
        Assert.All(document.Descendants().Attributes("Ccy"), currency => Assert.Equal("GEL", currency.Value));
    }

    // Formats follow the schemas: Georgian and other non-ASCII text in identifiers is schema-valid and signs.
    [Fact]
    public void Non_ascii_identifiers_and_names_are_schema_valid_and_signed()
    {
        var r = Request();
        var xml = Build(r with
        {
            InstructionId = new string('ქ', 35),
            EndToEndId = "E2E/ქართ€<&>",
            Debtor = r.Debtor! with { Name = "ქართული «სახელი» — ü" }
        });
        Pacs008Schema.Validate(xml);
        using var certificate = IpsReplies.Certificate();
        var signed = new Infrastructure.Payments.Pacs008.Signing.Pacs008MessageSigner(new(false, false), TimeProvider.System).Prepare(xml, certificate);
        Assert.True(SignatureVerifier.Verifies(signed.Xml, certificate));
        Assert.Equal("E2E/ქართ€<&>", XDocument.Parse(signed.Xml).Descendants(P + "EndToEndId").Single().Value);
    }

    private static string Build(Pacs008Request input)
    {
        var result = ValidatedPacs008.Validate(input, Policy);
        Assert.Empty(result.Errors);
        return new Pacs008Xml(new("NBGEGE22")).Build(result.Payment!, Context);
    }
}
