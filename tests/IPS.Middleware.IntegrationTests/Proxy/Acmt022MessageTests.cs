using System.Xml.Linq;
using IPS.Middleware.Application.Proxy;
using IPS.Middleware.Infrastructure.Proxy;
using IPS.Middleware.IntegrationTests.Payments;
using Xunit;
using static IPS.Middleware.IntegrationTests.Proxy.ProxyFixture;

namespace IPS.Middleware.IntegrationTests.Proxy;

public sealed class Acmt022MessageTests
{
    private static readonly ProxyIds Ids = new("op-1", "bulk-1");
    private static readonly XNamespace Wrapper = "urn:montran:message.01";

    [Fact]
    public async Task A_full_registration_is_schema_valid_with_the_exact_party_account_agent_and_supplementary_data()
    {
        var xml = await Protocol().PrepareRegisterAsync(Register(), Ids, default);

        ProxySchema.Validate(xml);
        var document = XDocument.Parse(xml);
        Assert.Equal(Wrapper + "Message", document.Root!.Name);
        var header = document.Descendants(Head + "AppHdr").Single();
        Assert.Equal("bulk-1", header.Element(Head + "BizMsgIdr")!.Value);
        Assert.Equal("acmt.022.001.04", header.Element(Head + "MsgDefIdr")!.Value);
        Assert.Equal("2026-10-07T08:30:15Z", header.Element(Head + "CreDt")!.Value);
        Assert.Null(header.Element(Head + "Sgntr"));
        var assignment = document.Descendants(Acmt + "Assgnmt").Single();
        Assert.Equal(("bulk-1", Participant, ProxyBic), (
            assignment.Element(Acmt + "MsgId")!.Value,
            assignment.Element(Acmt + "Assgnr")!.Descendants(Acmt + "BICFI").Single().Value,
            assignment.Element(Acmt + "Assgne")!.Descendants(Acmt + "BICFI").Single().Value));
        var modification = document.Descendants(Acmt + "Mod").Single();
        Assert.Equal("op-1", modification.Element(Acmt + "Id")!.Value);
        var updated = modification.Element(Acmt + "UpdtdPtyAndAcctId")!;
        Assert.Equal("01001011111", updated.Element(Acmt + "Pty")!.Element(Acmt + "Id")!.Element(Acmt + "PrvtId")!.Element(Acmt + "Othr")!.Element(Acmt + "Id")!.Value);
        Assert.Equal("გიორგი", document.Descendants(Acmt + "IndvPrsn").Single().Element(Acmt + "GvnNm")!.Value);
        Assert.Equal(["MBNO", "EMAL"], updated.Descendants(Acmt + "ChanlTp").Select(element => element.Value));
        Assert.Equal(["+995599123456", "a@b.ge"], updated.Element(Acmt + "Pty")!.Element(Acmt + "CtctDtls")!.Descendants(Acmt + "Id").Select(element => element.Value));
        var account = updated.Element(Acmt + "Acct")!;
        Assert.Equal(Iban, account.Element(Acmt + "Id")!.Element(Acmt + "IBAN")!.Value);
        Assert.Equal("CACC", account.Element(Acmt + "Tp")!.Element(Acmt + "Prtry")!.Value);
        Assert.Equal("GEL", account.Element(Acmt + "Ccy")!.Value);
        Assert.Equal(Participant, updated.Element(Acmt + "Agt")!.Descendants(Acmt + "BICFI").Single().Value);
        var details = document.Descendants(Acmt + "ModAddtlInf").Single();
        Assert.Equal(["Id", "Pty", "Acct", "AuthPer", "Bnfcry"], details.Elements().Select(element => element.Name.LocalName));
        Assert.Equal(["GvnNm", "MddlNm", "Srnm", "Gndr", "CtryOfRes", "Ctznsh"], details.Element(Acmt + "Pty")!.Element(Acmt + "IndvPrsn")!.Elements().Select(element => element.Name.LocalName));
        Assert.Equal("MALE", details.Element(Acmt + "Pty")!.Descendants(Acmt + "Gndr").Single().Value);
        Assert.Equal("FEMA", details.Element(Acmt + "Bnfcry")!.Element(Acmt + "Gndr")!.Value);
        Assert.Equal(["2027-01-01"], details.Element(Acmt + "Bnfcry")!.Elements(Acmt + "ToDt").Select(element => element.Value));
    }

    [Fact]
    public async Task A_holder_only_registration_and_a_legal_entity_are_valid_without_contact_details_or_account()
    {
        var minimal = new RegisterProxyRequest(Holder("LegalEntity"), null, null, null, null);

        var xml = await Protocol().PrepareRegisterAsync(minimal, Ids, default);

        ProxySchema.Validate(xml);
        var document = XDocument.Parse(xml);
        Assert.Empty(document.Descendants(Acmt + "CtctDtls"));
        Assert.Empty(document.Descendants(Acmt + "Acct"));
        Assert.Empty(document.Descendants(Acmt + "Agt").Where(agent => agent.Parent!.Name.LocalName == "UpdtdPtyAndAcctId"));
        Assert.NotNull(document.Descendants(Acmt + "OrgId").SingleOrDefault());
        Assert.Equal(["LglForm", "Nm", "WlB", "RegnCtry"], document.Descendants(Acmt + "Org").Single().Elements().Select(element => element.Name.LocalName));
        Assert.Equal("true", document.Descendants(Acmt + "WlB").Single().Value);
    }

    [Fact]
    public async Task A_registration_with_a_non_iban_account_sends_the_other_identifier()
    {
        var request = Register() with { Account = new ProxyAccount("PSP-123", false, "GEL", null, null, null) };

        var xml = await Protocol().PrepareRegisterAsync(request, Ids, default);

        ProxySchema.Validate(xml);
        Assert.Equal("PSP-123", XDocument.Parse(xml).Descendants(Acmt + "Acct").First().Element(Acmt + "Id")!.Element(Acmt + "Othr")!.Element(Acmt + "Id")!.Value);
    }

    [Fact]
    public async Task An_update_sends_the_original_party_and_only_what_changed()
    {
        var xml = await Protocol().PrepareUpdateAsync(Update(), Ids, default);

        ProxySchema.Validate(xml);
        var document = XDocument.Parse(xml);
        var modification = document.Descendants(Acmt + "Mod").Single();
        Assert.Equal(["Id", "OrgnlPtyAndAcctId", "UpdtdPtyAndAcctId"], modification.Elements().Select(element => element.Name.LocalName));
        Assert.Equal(["Pty"], modification.Element(Acmt + "OrgnlPtyAndAcctId")!.Elements().Select(element => element.Name.LocalName));
        // The account override is sent as an IBAN with its currency, and the agent follows the account.
        Assert.Equal(["Pty", "Acct", "Agt"], modification.Element(Acmt + "UpdtdPtyAndAcctId")!.Elements().Select(element => element.Name.LocalName));
        var person = document.Descendants(Acmt + "AuthPer").Single();
        Assert.Equal(["Nm", "Id", "FrDt"], person.Elements().Select(element => element.Name.LocalName));
        Assert.Equal("Given Middle Surname", person.Element(Acmt + "Nm")!.Value);
        // The updated holder carries details only; dates the caller did not send are absent.
        Assert.Empty(document.Descendants(Acmt + "ClsgDt"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task A_blank_account_override_in_an_update_means_no_account(string blank)
    {
        var request = Update() with { AccountIdentifier = blank };

        var xml = await Protocol().PrepareUpdateAsync(request, Ids, default);

        ProxySchema.Validate(xml);
        Assert.Empty(XDocument.Parse(xml).Descendants(Acmt + "Acct"));
    }

    [Fact]
    public async Task An_update_with_nothing_to_change_still_has_the_mandatory_empty_details()
    {
        var request = new UpdateProxyRequest("01001011111", "LegalEntity", null, null, null, null, null, null, null);

        var xml = await Protocol().PrepareUpdateAsync(request, Ids, default);

        ProxySchema.Validate(xml);
        var document = XDocument.Parse(xml);
        Assert.Empty(document.Descendants(Acmt + "Acct"));
        Assert.Empty(document.Descendants(Acmt + "ModAddtlInf"));
        Assert.Single(document.Descendants(Acmt + "Dtls"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_removal_keeps_the_account_only_when_asked_to(bool keepAccountActive)
    {
        var xml = await Protocol().PrepareRemoveAsync(Remove(keepAccountActive), Ids, default);

        ProxySchema.Validate(xml);
        var document = XDocument.Parse(xml);
        var updated = document.Descendants(Acmt + "UpdtdPtyAndAcctId").Single();
        Assert.Equal(keepAccountActive, updated.Element(Acmt + "Acct") is not null);
        var original = document.Descendants(Acmt + "OrgnlPtyAndAcctId").Single();
        Assert.Equal(["Pty", "Acct", "Agt"], original.Elements().Select(element => element.Name.LocalName));
        Assert.Equal("+995599123456", original.Descendants(Acmt + "CtctDtls").Single().Descendants(Acmt + "Id").Single().Value);
        // As the source sends them: authorized persons as AuthPer, beneficial owners as BfyOwnr.
        var details = document.Descendants(Acmt + "ModAddtlInf").Single();
        Assert.Equal(["Id", "AuthPer", "BfyOwnr"], details.Elements().Select(element => element.Name.LocalName));
    }

    [Fact]
    public async Task A_removal_of_a_whole_account_with_nothing_else_has_no_supplementary_details_or_contacts()
    {
        var request = new RemoveProxyRequest("01001011111", "Individual", Iban, "GEL", false, null, null, null);

        var xml = await Protocol().PrepareRemoveAsync(request, Ids, default);

        ProxySchema.Validate(xml);
        var document = XDocument.Parse(xml);
        Assert.Empty(document.Descendants(Acmt + "CtctDtls"));
        Assert.Empty(document.Descendants(Acmt + "SplmtryData"));
    }

    [Fact]
    public async Task Text_with_xml_special_characters_is_escaped_and_the_message_stays_valid()
    {
        var request = Register() with { AccountHolder = Holder() with { GivenName = "A & <B> \"C\"" } };

        var xml = await Protocol().PrepareRegisterAsync(request, Ids, default);

        ProxySchema.Validate(xml);
        Assert.Equal("A & <B> \"C\"", XDocument.Parse(xml).Descendants(Acmt + "IndvPrsn").Single().Element(Acmt + "GvnNm")!.Value);
    }

    [Fact]
    public async Task Without_a_certificate_the_message_is_sent_unsigned_as_in_the_source()
    {
        var xml = await Protocol().PrepareRegisterAsync(Register(), Ids, default);

        Assert.DoesNotContain("Signature", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Sgntr", xml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("register")]
    [InlineData("update")]
    [InlineData("remove")]
    public async Task With_a_certificate_the_message_is_signed_and_the_signature_verifies_independently(string operation)
    {
        using var certificate = IpsReplies.Certificate();
        var protocol = Protocol(new SingleCertificate(certificate));

        var xml = operation switch
        {
            "register" => await protocol.PrepareRegisterAsync(Register(), Ids, default),
            "update" => await protocol.PrepareUpdateAsync(Update(), Ids, default),
            _ => await protocol.PrepareRemoveAsync(Remove(), Ids, default)
        };

        ProxySchema.Validate(xml);
        Assert.NotNull(XDocument.Parse(xml).Descendants(Head + "Sgntr").SingleOrDefault());
        Assert.True(SignatureVerifier.Verifies(xml, certificate));
        Assert.False(SignatureVerifier.Verifies(xml.Replace("bulk-1", "bulk-2", StringComparison.Ordinal), certificate));
    }

    [Fact]
    public async Task Every_call_gets_fresh_references_from_the_management_workflow()
    {
        var first = ProxyIds.New();
        var second = ProxyIds.New();

        Assert.NotEqual(first.OperationId, second.OperationId);
        Assert.NotEqual(first.BulkMessageId, second.BulkMessageId);
        Assert.DoesNotContain(' ', first.OperationId);
        Assert.Equal(32, first.BulkMessageId.Length);
        await Task.CompletedTask;
    }
}
