namespace IPS.Middleware.Application.Payments.Pacs008;

public sealed class Pacs008Policy
{
    public Pacs008Policy(string participantBic, string? treasuryBic, IEnumerable<PaymentCurrency> currencies,
        IEnumerable<string>? indirectParticipants = null)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(participantBic, Pacs008Text.Bic))
            throw new ArgumentException("A participant BIC is required.", nameof(participantBic));
        ParticipantBic = participantBic;
        TreasuryBic = string.IsNullOrWhiteSpace(treasuryBic) ? null : treasuryBic.Trim();
        Currencies = Array.AsReadOnly(currencies.ToArray());
        if (Currencies.Count == 0 || Currencies.Any(c => c.Code.Length != 3 || !c.Code.All(char.IsAsciiLetterUpper) ||
                c.Minimum < 0 || c.Maximum < 0 || c.Minimum > c.Maximum) ||
            Currencies.Select(c => c.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Currencies.Count)
            throw new ArgumentException("Configure distinct currencies with valid amount limits.", nameof(currencies));
        IndirectParticipants = Array.AsReadOnly((indirectParticipants ?? []).Select(p => p.Trim()).ToArray());
    }

    internal PaymentCurrency? FindCurrency(string? code) => Currencies.FirstOrDefault(currency =>
        string.Equals(currency.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));

    internal bool IsTreasury(string? bic) => TreasuryBic is not null &&
        string.Equals(bic?.Trim(), TreasuryBic, StringComparison.OrdinalIgnoreCase);

    public string ParticipantBic { get; }
    public string? TreasuryBic { get; }
    public IReadOnlyList<PaymentCurrency> Currencies { get; }
    public IReadOnlyList<string> IndirectParticipants { get; }
}

public sealed record PaymentCurrency(string Code, bool Enabled = true, decimal? Minimum = null, decimal? Maximum = null);
