using IPS.Middleware.Domain.Transactions;
using Xunit;

namespace IPS.Middleware.Tests.Transactions;

public sealed class IpsStatusCodesTests
{
    [Fact]
    public void The_catalog_holds_each_annex_2_code_once()
    {
        Assert.Equal(47, IpsStatusCodes.All.Count);
        Assert.Equal(IpsStatusCodes.All.Count, IpsStatusCodes.All.Select(code => code.InternalCode).Distinct().Count());
    }

    [Theory]
    [InlineData(0, null, IpsStatusCodeAction.Accept)]
    [InlineData(1009, "AC01", IpsStatusCodeAction.FinalReject)]
    [InlineData(1016, "AG09", IpsStatusCodeAction.Resend)]
    [InlineData(1017, "AG09", IpsStatusCodeAction.InvestigateLater)]
    [InlineData(2000, "AM23", IpsStatusCodeAction.FinalReject)]
    public void A_code_gives_its_iso_reason_and_action(int internalCode, string? isoCode, IpsStatusCodeAction action)
    {
        var code = IpsStatusCodes.Find(internalCode);

        Assert.Equal((isoCode, action), (code!.IsoCode, code.Action));
    }

    [Fact]
    public void A_code_annex_2_does_not_list_is_not_found()
    {
        Assert.Null(IpsStatusCodes.Find(9999));
    }
}
