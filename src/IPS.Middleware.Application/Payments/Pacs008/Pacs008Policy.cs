using System.Text.RegularExpressions;

namespace IPS.Middleware.Application.Payments.Pacs008;

public sealed class Pacs008Policy
{
    public Pacs008Policy(
        string participantBic,
        string? treasuryBic,
        IEnumerable<PaymentCurrency> currencies,
        IEnumerable<string>? indirectParticipants = null)
    {
        if (!Regex.IsMatch(participantBic, Pacs008Text.Bic))
        {
            throw new ArgumentException("A participant BIC is required.", nameof(participantBic));
        }

        var configuredCurrencies = currencies.ToArray();
        if (!AreValid(configuredCurrencies))
        {
            throw new ArgumentException("Configure distinct currencies with valid amount limits.", nameof(currencies));
        }

        ParticipantBic = participantBic;
        TreasuryBic = string.IsNullOrWhiteSpace(treasuryBic) ? null : treasuryBic.Trim();
        Currencies = Array.AsReadOnly(configuredCurrencies);
        IndirectParticipants = Array.AsReadOnly((indirectParticipants ?? []).Select(participant => participant.Trim()).ToArray());
    }

    public string ParticipantBic { get; }
    public string? TreasuryBic { get; }
    public IReadOnlyList<PaymentCurrency> Currencies { get; }
    public IReadOnlyList<string> IndirectParticipants { get; }

    internal PaymentCurrency? FindCurrency(string? code) =>
        Currencies.FirstOrDefault(currency => string.Equals(currency.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));

    internal bool IsTreasury(string? bic) =>
        TreasuryBic is not null && string.Equals(bic?.Trim(), TreasuryBic, StringComparison.OrdinalIgnoreCase);

    private static bool AreValid(IReadOnlyCollection<PaymentCurrency> currencies)
    {
        var distinctCodes = currencies
            .Select(currency => currency.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return currencies.Count > 0
            && distinctCodes == currencies.Count
            && currencies.All(currency => currency.HasValidCode && currency.HasValidLimits);
    }
}

public sealed record PaymentCurrency(string Code, bool Enabled = true, decimal? MinAmount = null, decimal? MaxAmount = null)
{
    internal bool HasValidCode => Code.Length == 3 && Code.All(char.IsAsciiLetterUpper);

    internal bool HasValidLimits => MinAmount is null or > 0
        && MaxAmount is null or > 0
        && !(MinAmount > MaxAmount);
}
