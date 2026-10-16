using IPS.Middleware.Application.Payments;
using Xunit;

namespace IPS.Middleware.Tests.Payments;

public sealed class BankIdentityTests
{
    [Fact]
    public void Codes_are_trimmed_and_upper_case()
    {
        var bank = new BankIdentity(" itsbge22 ", "geatsaaa", " tresge22");

        Assert.Equal("ITSBGE22", bank.Bic);
        Assert.Equal("GEATSAAA", bank.NbgBic);
        Assert.Equal("TRESGE22", bank.TreasuryBic);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void The_treasury_code_is_optional(string? treasury)
    {
        var bank = new BankIdentity("ITSBGE22", "GEATSAAA", treasury);

        Assert.Null(bank.TreasuryBic);
        Assert.False(bank.IsTreasury(null));
        Assert.False(bank.IsTreasury(""));
    }

    [Fact]
    public void Each_code_is_recognized_ignoring_case_and_surrounding_spaces()
    {
        var bank = new BankIdentity("ITSBGE22", "GEATSAAA", "TRESGE22");

        Assert.True(bank.IsOwn(" itsbge22 "));
        Assert.True(bank.IsNbg("geatsaaa"));
        Assert.True(bank.IsTreasury("TresGE22"));
        Assert.False(bank.IsOwn("GEATSAAA"));
        Assert.False(bank.IsNbg("ITSBGE22"));
        Assert.False(bank.IsTreasury("ITSBGE22"));
        Assert.False(bank.IsOwn(null));
    }

    [Theory]
    [InlineData("", "GEATSAAA", null)]
    [InlineData("ITSBGE2", "GEATSAAA", null)]
    [InlineData("ITSBGE22X", "GEATSAAA", null)]
    [InlineData("ITSB-E22", "GEATSAAA", null)]
    [InlineData("ITSBGE22", "", null)]
    [InlineData("ITSBGE22", "BAD", null)]
    [InlineData("ITSBGE22", "GEATSAAA", "BAD")]
    [InlineData("ITSBGE22", "itsbge22", null)]
    [InlineData("ITSBGE22", "GEATSAAA", "ITSBGE22")]
    [InlineData("ITSBGE22", "GEATSAAA", "geatsaaa")]
    public void Invalid_or_equal_codes_are_refused(string bic, string nbg, string? treasury) =>
        Assert.Throws<ArgumentException>(() => new BankIdentity(bic, nbg, treasury));

    [Fact]
    public void Eleven_character_codes_are_accepted()
    {
        var bank = new BankIdentity("ITSBGE22XXX", "GEATSAAAXXX");

        Assert.Equal("ITSBGE22XXX", bank.Bic);
    }
}
