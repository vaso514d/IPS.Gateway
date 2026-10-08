using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using IPS.Middleware.IntegrationTests.Payments;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

public sealed class IncomingPacs008Fixture : IAsyncLifetime
{
    internal X509Certificate2 Certificate { get; } = IpsReplies.Certificate(validAt: new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
    internal Dictionary<string, string> Signed { get; } = [];
    internal static readonly XNamespace Pacs = "urn:iso:std:iso:20022:tech:xsd:pacs.008.001.12";
    internal static readonly XNamespace Head = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";

    public async Task InitializeAsync()
    {
        var inputs = new Dictionary<string, string> { ["valid"] = Xml };
        void Add(string name, Action<XDocument> change)
        {
            var document = XDocument.Parse(Xml, LoadOptions.PreserveWhitespace);
            change(document);
            inputs[name] = document.ToString(SaveOptions.DisableFormatting);
        }
        Add("minimal", d =>
        {
            foreach (var name in new[] { "PmtTpInf", "InstrId", "UETR", "AccptncDtTm", "UltmtDbtr", "UltmtCdtr", "RgltryRptg", "RltdRmtInf", "RmtInf", "PstlAdr", "ClrSysMmbId" })
            {
                d.Descendants(Pacs + name).Remove();
            }
        });
        Add("batch", d => d.Descendants(Pacs + "CdtTrfTxInf").Single().AddAfterSelf(new XElement(d.Descendants(Pacs + "CdtTrfTxInf").Single())));
        Add("count-supplement", d =>
        {
            d.Descendants(Pacs + "NbOfTxs").Single().Value = "2";
            d.Descendants(Pacs + "FIToFICstmrCdtTrf").Single().Add(new XElement(Pacs + "SplmtryData",
                new XElement(Pacs + "Envlp", new XElement(XName.Get("Evidence", "urn:fixture"), "unchanged"))));
        });
        Add("count-wrong-order", d =>
        {
            d.Descendants(Pacs + "NbOfTxs").Single().Value = "2";
            var transfer = d.Descendants(Pacs + "CdtTrfTxInf").Single();
            transfer.Remove();
            d.Descendants(Pacs + "GrpHdr").Single().AddBeforeSelf(transfer);
        });
        Add("batch-wrong-second-order", d =>
        {
            var group = d.Descendants(Pacs + "FIToFICstmrCdtTrf").Single();
            group.Add(new XElement(Pacs + "SplmtryData", new XElement(Pacs + "Envlp", new XElement(XName.Get("Evidence", "urn:fixture"), "data"))));
            group.Add(new XElement(d.Descendants(Pacs + "CdtTrfTxInf").Single()));
        });
        Add("count", d => d.Descendants(Pacs + "NbOfTxs").Single().Value = "2");
        Add("missing-count", d => d.Descendants(Pacs + "NbOfTxs").Single().Remove());
        Add("invalid-count", d => d.Descendants(Pacs + "NbOfTxs").Single().Value = "invalid");
        Add("missing-date-and-count", d => { d.Descendants(Pacs + "GrpHdr").Single().Element(Pacs + "CreDtTm")!.Remove(); d.Descendants(Pacs + "NbOfTxs").Single().Remove(); });
        Add("missing-id", d => d.Descendants(Pacs + "EndToEndId").Single().Remove());
        Add("bad-amount", d => d.Descendants(Pacs + "IntrBkSttlmAmt").Single().Value = "not-money");
        Add("wrong-version", d => d.Descendants(Head + "MsgDefIdr").Single().Value = "pacs.008.001.11");
        Add("wrong-name", d => d.Descendants(Head + "MsgDefIdr").Single().Value = "pacs.0089");
        inputs["wrong-namespace"] = Xml.Replace(Pacs.NamespaceName, "urn:wrong", StringComparison.Ordinal);
        Add("extra-header", d => { var extra = new XElement(d.Root!.Elements().First()); extra.Element(Head + "Sgntr")!.Remove(); d.Root.AddFirst(extra); });
        Add("batch-bad-second", d => { var second = new XElement(d.Descendants(Pacs + "CdtTrfTxInf").Single()); second.Element(Pacs + "IntrBkSttlmAmt")!.Value = "bad"; d.Descendants(Pacs + "CdtTrfTxInf").Single().AddAfterSelf(second); });
        var signed = await IpsReplies.SignAsync(Certificate, inputs.Values.ToArray());
        foreach (var pair in inputs.Keys.Zip(signed))
        {
            Signed.Add(pair.First, pair.Second);
        }
    }
    public Task DisposeAsync()
    {
        Certificate.Dispose();
        return Task.CompletedTask;
    }

    // Hand-authored protocol fixture, independent of the production outgoing XML builder.
    internal const string Xml = """
        <Message>
          <AppHdr xmlns="urn:iso:std:iso:20022:tech:xsd:head.001.001.03">
            <Fr><FIId><FinInstnId><BICFI>NBGEGE22</BICFI></FinInstnId></FIId></Fr>
            <To><FIId><FinInstnId><BICFI>BAGAGE22</BICFI></FinInstnId></FIId></To>
            <BizMsgIdr>IN-HEADER-1</BizMsgIdr><MsgDefIdr>pacs.008.001.12</MsgDefIdr><CreDt>2026-10-04T12:00:00Z</CreDt><Sgntr/>
          </AppHdr>
          <Document xmlns="urn:iso:std:iso:20022:tech:xsd:pacs.008.001.12"><FIToFICstmrCdtTrf>
            <GrpHdr><MsgId>IN-GROUP-1</MsgId><CreDtTm>2026-10-04T12:00:00Z</CreDtTm><NbOfTxs>1</NbOfTxs>
              <IntrBkSttlmDt>2026-10-04</IntrBkSttlmDt><SttlmInf><SttlmMtd>CLRG</SttlmMtd></SttlmInf>
            </GrpHdr>
            <CdtTrfTxInf>
              <PmtId><InstrId>INSTRUCTION-1</InstrId><EndToEndId>E2E-1</EndToEndId><TxId>TX-1</TxId><UETR>12345678-1234-4234-8234-123456789012</UETR></PmtId>
              <PmtTpInf><InstrPrty>HIGH</InstrPrty><SvcLvl><Cd>INST</Cd></SvcLvl><LclInstrm><Cd>INST</Cd></LclInstrm><CtgyPurp><Cd>SUPP</Cd></CtgyPurp></PmtTpInf>
              <IntrBkSttlmAmt Ccy="GEL">12.50</IntrBkSttlmAmt><AccptncDtTm>2026-10-04T16:00:00+04:00</AccptncDtTm><ChrgBr>SLEV</ChrgBr>
              <UltmtDbtr><Nm>Ultimate payer</Nm><Id><OrgId><LEI>12345678901234567890</LEI></OrgId></Id></UltmtDbtr>
              <Dbtr><Nm>Payer &amp; Co</Nm><PstlAdr><StrtNm>Main</StrtNm><BldgNb>1</BldgNb><PstCd>0100</PstCd><TwnNm>Tbilisi</TwnNm><CtrySubDvsn>Region</CtrySubDvsn><Ctry>GE</Ctry><AdrLine>Part 1</AdrLine><AdrLine>Part 2</AdrLine></PstlAdr>
                <Id><OrgId><Othr><Id>ORG-1</Id></Othr><Othr><Id>BILL-1</Id><SchmeNm><Cd>BILL</Cd></SchmeNm></Othr></OrgId></Id>
              </Dbtr>
              <DbtrAcct><Id><IBAN>GE29NB0000000101904917</IBAN></Id></DbtrAcct>
              <DbtrAgt><FinInstnId><BICFI>ITSBGE22</BICFI><ClrSysMmbId><ClrSysId><Cd>GE</Cd></ClrSysId><MmbId>SENDER-MEMBER</MmbId></ClrSysMmbId></FinInstnId></DbtrAgt>
              <CdtrAgt><FinInstnId><BICFI>BAGAGE22</BICFI><ClrSysMmbId><ClrSysId><Cd>GE</Cd></ClrSysId><MmbId>RECEIVER-MEMBER</MmbId></ClrSysMmbId></FinInstnId></CdtrAgt>
              <Cdtr><Nm>Payee</Nm><Id><PrvtId><Othr><Id>PERSON-2</Id></Othr></PrvtId></Id></Cdtr>
              <CdtrAcct><Id><Othr><Id>TREASURY-1</Id></Othr></Id></CdtrAcct>
              <UltmtCdtr><Nm>Ultimate payee</Nm><Id><OrgId><AnyBIC>TBCBGE22</AnyBIC></OrgId></Id></UltmtCdtr>
              <RgltryRptg><Dtls><Cd>QR</Cd><Inf>41.7</Inf><Inf>44.8</Inf></Dtls></RgltryRptg>
              <RltdRmtInf><RmtId>MOBL:PRXY:BILL</RmtId><RmtLctnDtls><Mtd>URID</Mtd><ElctrncAdr>device:42</ElctrncAdr></RmtLctnDtls></RltdRmtInf>
              <RltdRmtInf><RmtId>MOBL:BILL</RmtId></RltdRmtInf>
              <RmtInf><Ustrd>Invoice </Ustrd><Ustrd>42</Ustrd>
                <Strd><CdtrRefInf><Tp><CdOrPrtry><Prtry>MCC</Prtry></CdOrPrtry><Issr>Issuer</Issr></Tp><Ref>5411</Ref></CdtrRefInf><AddtlRmtInf>Part A</AddtlRmtInf><AddtlRmtInf>Part B</AddtlRmtInf></Strd>
                <Strd><CdtrRefInf><Tp><CdOrPrtry><Prtry>SERV</Prtry></CdOrPrtry></Tp><Ref>ORDER-1</Ref></CdtrRefInf></Strd>
              </RmtInf>
            </CdtTrfTxInf>
          </FIToFICstmrCdtTrf></Document>
        </Message>
        """;
}
