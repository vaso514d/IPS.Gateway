using IPS.Middleware.Application.Proxy;
using IPS.Middleware.Infrastructure.Proxy;
using Xunit;
using static IPS.Middleware.IntegrationTests.Proxy.ProxyFixture;

namespace IPS.Middleware.IntegrationTests.Proxy;

public sealed class ProxyReplyReaderTests
{
    [Fact]
    public void An_accepted_item_is_accepted()
    {
        Assert.Equal(ProxyOutcome.Accept(), ProxyReplyReader.Read(Accepted("op-1"), "op-1"));
    }

    [Fact]
    public void A_rejected_item_carries_its_code_and_description()
    {
        var outcome = ProxyReplyReader.Read(Rejected("op-1", "AM05", "Alias already registered"), "op-1");

        Assert.Equal(ProxyOutcome.Reject("AM05", "Alias already registered"), outcome);
    }

    [Theory]
    [InlineData("AM05", "Duplicate alias")]
    [InlineData("ZZ99", "Unrecognized Proxy error code 'ZZ99'.")]
    [InlineData(null, "Unknown error.")]
    public void A_reject_without_a_description_uses_the_error_code_table(string? code, string description)
    {
        var outcome = ProxyReplyReader.Read(Rejected("op-1", code, null), "op-1");

        Assert.Equal(ProxyOutcome.Reject(code, description), outcome);
    }

    [Fact]
    public void A_bulk_level_reject_decides_before_any_item()
    {
        var outcome = ProxyReplyReader.Read(BulkRejected("FF01", "Invalid XML"), "op-1");

        Assert.Equal(ProxyOutcome.Reject("FF01", "Invalid XML"), outcome);
    }

    [Fact]
    public void The_item_with_the_operation_id_decides_and_the_first_item_is_the_fallback()
    {
        var reply = Accepted("other").Replace("</FIToFIPmtStsRpt>",
            "<TxInfAndSts><OrgnlTxId>op-1</OrgnlTxId><TxSts>RJCT</TxSts><StsRsnInf><Rsn><Cd>BE18</Cd></Rsn></StsRsnInf></TxInfAndSts></FIToFIPmtStsRpt>", StringComparison.Ordinal);

        Assert.Equal("BE18", ProxyReplyReader.Read(reply, "op-1").ErrorCode);
        Assert.True(ProxyReplyReader.Read(reply, "unknown").Accepted);
    }

    [Theory]
    [InlineData("not xml")]
    [InlineData("<Message><Document /></Message>")]
    public void An_unreadable_answer_is_a_reject_with_the_internal_error_code(string xml)
    {
        var outcome = ProxyReplyReader.Read(xml, "op-1");

        Assert.False(outcome.Accepted);
        Assert.Equal("MS03", outcome.ErrorCode);
    }

    [Fact]
    public void An_accepted_group_without_items_is_a_reject_with_the_internal_error_code()
    {
        var outcome = ProxyReplyReader.Read(
            "<Message><Document><FIToFIPmtStsRpt><OrgnlGrpInfAndSts><GrpSts>ACCP</GrpSts></OrgnlGrpInfAndSts></FIToFIPmtStsRpt></Document></Message>", "op-1");

        Assert.Equal(ProxyOutcome.Reject("MS03", "Proxy response contained no matching operation result."), outcome);
    }

    [Fact]
    public void Prefixed_elements_and_a_lowercase_status_are_read_by_local_name()
    {
        const string xml = "<p:Message xmlns:p=\"urn:x\"><p:Document><p:FIToFIPmtStsRpt><p:TxInfAndSts><p:OrgnlTxId>op-1</p:OrgnlTxId><p:TxSts>accp</p:TxSts></p:TxInfAndSts></p:FIToFIPmtStsRpt></p:Document></p:Message>";

        Assert.True(ProxyReplyReader.Read(xml, "op-1").Accepted);
    }
}
