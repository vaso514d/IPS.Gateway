namespace IPS.MiidleWear.CoreAPI.Sending;

// Where CoreAPI sends the payments it generates, and the values that make them pass the gateway's validation.
public sealed class GatewayOptions
{
    public const string Section = "Gateway";

    // The gateway's base URL (IPS.Middleware.Api).
    public string BaseUrl { get; set; } = "http://localhost:5080";

    // The bank's own BIC: the debtor agent, and the gateway's Bank:BicCode.
    public string ParticipantBic { get; set; } = "ITSBGE22";

    // Other participants, picked at random as the creditor bank.
    public string[] CreditorBics { get; set; } = ["HABGGE22", "TBCBGE22", "BAGAGE22", "PAVEGE22", "LBRTGE22", "JSCRGE22"];

    public string[] Currencies { get; set; } = ["GEL"];

    // Picked at random when no amount is given; within the gateway's GEL limit.
    public decimal[] Amounts { get; set; } = [10m, 25m, 50m, 100m, 250m, 500m];

    // A send answers 200 when final or 504 after the gateway's wait; this must exceed that wait.
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
}
