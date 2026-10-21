using IPS.MiidleWear.Security;

namespace IPS.MiidleWear.CoreAPI.Sending;

// Where CoreAPI sends the payments it generates, and the values that make them pass the gateway's validation.
public sealed class GatewayOptions
{
    public const string Section = "Gateway";

    // The gateway's base URL (IPS.Middleware.Api).
    public string BaseUrl { get; set; } = "http://localhost:5080";

    // The bank's own BIC: the debtor agent, and the gateway's Bank:BicCode.
    public string ParticipantBic { get; set; } = "ITSBGE22";

    // The two letters after the check digits in this bank's IBANs (GEkk BB ...).
    public string ParticipantIbanCode { get; set; } = "";

    // Other participants by BIC, with their two-letter IBAN bank code: picked at random as the creditor bank and offered in
    // Swagger UI. Configuration binding appends to a collection's initial items, so the lists come from appsettings.json only.
    public Dictionary<string, string> CreditorBanks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string[] Currencies { get; set; } = [];

    // Recall reasons (camt.056), offered in Swagger UI and picked at random when none is given.
    public string[] RecallReasons { get; set; } = [];

    // Picked at random when no amount is given; within the gateway's GEL limit.
    public decimal[] Amounts { get; set; } = [10m, 25m, 50m, 100m, 250m, 500m];

    // A send answers 200 when final or 504 after the gateway's wait; this must exceed that wait.
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    // What this core sends with every request to the gateway (specification 021): a static API token, and its own
    // certificate (with the private key) that signs each body. Each is left out when not configured. What this core
    // requires of the gateway's calls is in the Server section.
    public string? ApiToken { get; set; }
    public IpsSigningSettings Signing { get; set; } = new();

    // A bank not configured with a code falls back to the first two letters of its BIC.
    public string IbanCode(string bic)
    {
        var configured = string.Equals(bic, ParticipantBic, StringComparison.OrdinalIgnoreCase)
            ? ParticipantIbanCode
            : CreditorBanks.GetValueOrDefault(bic);
        return string.IsNullOrWhiteSpace(configured) ? bic[..2] : configured.Trim().ToUpperInvariant();
    }
}
