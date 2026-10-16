using System.Text.RegularExpressions;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Payments;

// The bank the gateway serves, the National Bank of Georgia that operates the IPS and the Proxy Solution, and the
// optional Treasury. Every message is sent as Bic, and every incoming payment must be addressed to it.
public sealed class BankIdentity
{
    public BankIdentity(string bic, string nbgBic, string? treasuryBic = null)
    {
        Bic = Normalize(bic, nameof(bic));
        NbgBic = Normalize(nbgBic, nameof(nbgBic));
        TreasuryBic = string.IsNullOrWhiteSpace(treasuryBic) ? null : Normalize(treasuryBic, nameof(treasuryBic));
        if (AreNotDistinct(Bic, NbgBic, TreasuryBic))
        {
            throw new ArgumentException("The bank, NBG and Treasury codes must differ.");
        }
    }

    public string Bic { get; }
    public string NbgBic { get; }
    public string? TreasuryBic { get; }

    public bool IsOwn(string? bic) => Matches(bic, Bic);

    public bool IsNbg(string? bic) => Matches(bic, NbgBic);

    public bool IsTreasury(string? bic) => TreasuryBic is not null && Matches(bic, TreasuryBic);

    private static bool Matches(string? bic, string code) =>
        string.Equals(bic?.Trim(), code, StringComparison.OrdinalIgnoreCase);

    private static bool AreNotDistinct(params string?[] codes)
    {
        var configured = codes.OfType<string>().ToArray();
        return configured.Distinct(StringComparer.Ordinal).Count() != configured.Length;
    }

    private static string Normalize(string? bic, string name)
    {
        var code = bic?.Trim().ToUpperInvariant();
        if (code is null || !Regex.IsMatch(code, Pacs008Text.Bic, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            throw new ArgumentException("An 8 or 11 character BIC is required.", name);
        }

        return code;
    }
}
