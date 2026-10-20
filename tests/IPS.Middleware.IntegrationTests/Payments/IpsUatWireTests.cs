using System.Xml.Linq;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Payments;

// The IPS UAT's own wire format. The reply below is the first answer the UAT gave a pacs.008 (2026-10-20): it could not
// parse our message, which then had no montran envelope, and said so with OrgnlMsgId PARSEERROR and RJCT/1013.
public sealed class IpsUatWireTests
{
    private const string Env = "urn:montran:message.01";
    private static readonly IpsReplyCorrelation Sent = new("a02fc9e67f4e41c6ba83126edf9b17cd", "TX-1", "E2E-1");

    [Fact]
    public void The_uat_parse_error_reply_settles_the_send_it_answered_as_rejected()
    {
        var reply = new IpsReplyInterpreter(new IpsSignatureTrust([], TimeProvider.System))
            .Interpret(new IpsSubmissionResponse(200, ParseErrorReply, [new("x-montran-ips-reqsts", "RJCT/1013")]), Sent, receivedAtUtc: null);

        Assert.Equal(IpsReplyStatus.Rejected, reply.Status);
        Assert.Equal(("FF01", (int?)1013), (reply.Details.ReasonCode, reply.Details.IpsInternalCode));
        Assert.Contains("Cannot find the declaration of element 'Message'", reply.Details.Description);
    }

    [Fact]
    public void A_parse_error_report_outside_the_send_identifies_no_payment()
    {
        var reply = new IpsReplyInterpreter(new IpsSignatureTrust([], TimeProvider.System))
            .Interpret(new IpsSubmissionResponse(200, ParseErrorReply, [new("x-montran-ips-reqsts", "RJCT/1013")]), Sent, PaymentMessageTypes.Pacs008Definition);

        Assert.Equal(IpsReplyStatus.Unresolved, reply.Status);
    }

    [Fact]
    public void A_built_pacs008_has_the_envelope_the_uat_sends()
    {
        var xml = new Pacs008Xml(new("NBGEGE22")).Build(
            ValidatedPacs008.Validate(Pacs008Fixture.Request(), Pacs008Fixture.Policy).Payment!,
            new PaymentMessageContext("MSG-1", "TX-1", Pacs008Fixture.Created));

        var root = XDocument.Parse(xml).Root!;
        Assert.Equal(XName.Get("Message", Env), root.Name);
        Assert.Equal(
            [XName.Get("AppHdr", Env), XName.Get("FIToFICstmrCdtTrf", Env)],
            root.Elements().Select(element => element.Name));
        Assert.All(root.Elements().First().Elements(), element => Assert.Equal(Pacs008Xml.HeaderNamespace, element.Name.NamespaceName));
        Assert.All(root.Elements().Last().Elements(), element => Assert.Equal(Pacs008Xml.DocumentNamespace, element.Name.NamespaceName));
        Assert.DoesNotContain(root.Descendants(), element => element.Name.LocalName == "Document");
    }

    private const string ParseErrorReply = """
        <?xml version="1.0" encoding="UTF-8"?>
        <hdr:Message xmlns:hdr="urn:montran:message.01">
        	<hdr:AppHdr xmlns="urn:iso:std:iso:20022:tech:xsd:head.001.001.03">
        		<Fr>
        			<FIId>
        				<FinInstnId>
        					<BICFI>IPSAGE22</BICFI>
        				</FinInstnId>
        			</FIId>
        		</Fr>
        		<To>
        			<FIId>
        				<FinInstnId>
        					<BICFI>ITSBGE22</BICFI>
        				</FinInstnId>
        			</FIId>
        		</To>
        		<BizMsgIdr>PS202609230000021701</BizMsgIdr>
        		<MsgDefIdr>pacs.002.001.14</MsgDefIdr>
        		<CreDt>2026-10-20T06:59:00Z</CreDt>
        		<Sgntr><dsig:Signature xmlns:dsig="http://www.w3.org/2000/09/xmldsig#"><dsig:SignedInfo><dsig:CanonicalizationMethod Algorithm="http://www.w3.org/2006/12/xml-c14n11"></dsig:CanonicalizationMethod><dsig:SignatureMethod Algorithm="http://www.w3.org/2001/04/xmldsig-more#rsa-sha256"></dsig:SignatureMethod><dsig:Reference URI=""><dsig:Transforms><dsig:Transform Algorithm="http://www.w3.org/2000/09/xmldsig#enveloped-signature"></dsig:Transform><dsig:Transform Algorithm="http://www.w3.org/2006/12/xml-c14n11"></dsig:Transform></dsig:Transforms><dsig:DigestMethod Algorithm="http://www.w3.org/2001/04/xmlenc#sha256"></dsig:DigestMethod><dsig:DigestValue>8TtyE9EVdJzBfveaYARXpzi2iv9MFMMQmFYwZLnNwyw=</dsig:DigestValue></dsig:Reference></dsig:SignedInfo><dsig:SignatureValue>op/nVPn920Y2nwndpOq87R3D5cRCeI9pCRnFZH8uIPYrcJiQnxS127my5i3v8AEj8bzfjPWmoXb8
        1n0i+NXmBBWupRxHlwr8+B6wWAAm3UXTnBStRc5unKzXMIyO72hOZ2Wo6wnfUTWALbuNVEfzj+JR
        /dBbWSr/svnqVtsXjdkFhcUXsRXddViwqFe8eNEfi44W2thJK+2KUOqE8w6sav0onwRZbCXI0bKO
        sULD+xx8tSGKnOebclHUA+p18uwW2daZruYwIZo++wRjWKtd88KxGexbdhZmuYzsqGR42Yg3iqzz
        edAodM3f8U78DqYBUB5yvoXtDpcEDU/9V7J0i/oRa0nQGM+1gTlGtjEooND/DgbGbE3RzQXz/wKD
        N69Ndeu5uWLhjsvuoPtsSyHpFfCiho6elseW0rimBPDlqFn1DtRnZrUqFh7P5iQ5xBr6ZxbwRJYJ
        ueHtIcT4WRAX/0XWsu0p0dH/dtAQhgOrpTYQ8tmomW1nC61cEGnIExP9dDuc05ZW9mMQYBNZ8Ugz
        CLfKrw+p+xJ8IcuXInnPPywjEyNLO+g2mWmTDcBmuW0KrL2jI9VOiDgAEgI1xR3eQEXHgjQFR+rG
        RGMvrldmqlIWlp6wi7l0v349mivG8rm0Z4lhNXN65PHxHhRw6niJXV+G3+ZVVBrfhc6ojDI6/kk=</dsig:SignatureValue><dsig:KeyInfo><dsig:X509Data><dsig:X509IssuerSerial><dsig:X509IssuerName>CN=IPS-Class2-SubCA-Test,DC=ips,DC=local</dsig:X509IssuerName><dsig:X509SerialNumber>2609187188252052086014521872294746331195375620</dsig:X509SerialNumber></dsig:X509IssuerSerial></dsig:X509Data></dsig:KeyInfo></dsig:Signature></Sgntr>
        	</hdr:AppHdr>
        	<hdr:FIToFIPmtStsRpt xmlns="urn:iso:std:iso:20022:tech:xsd:pacs.002.001.14">
        		<GrpHdr>
        			<MsgId>PS202609230000021701</MsgId>
        			<CreDtTm>2026-10-20T10:59:00+04:00</CreDtTm>
        			<InstdAgt>
        				<FinInstnId>
        					<BICFI>ITSBGE22</BICFI>
        				</FinInstnId>
        			</InstdAgt>
        		</GrpHdr>
        		<OrgnlGrpInfAndSts>
        			<OrgnlMsgId>PARSEERROR</OrgnlMsgId>
        			<OrgnlMsgNmId>pacs.008.001.12</OrgnlMsgNmId>
        			<GrpSts>RJCT</GrpSts>
        			<StsRsnInf>
        				<Orgtr>
        					<Id>
        						<OrgId>
        							<AnyBIC>IPSAGE22</AnyBIC>
        						</OrgId>
        					</Id>
        				</Orgtr>
        				<Rsn>
        					<Cd>FF01</Cd>
        				</Rsn>
        				<AddtlInf>1013: cvc-elt.1.a: Cannot find the declaration of element 'Message'. [L:1, C:130]</AddtlInf>
        			</StsRsnInf>
        		</OrgnlGrpInfAndSts>
        	</hdr:FIToFIPmtStsRpt>
        </hdr:Message>
        """;
}
