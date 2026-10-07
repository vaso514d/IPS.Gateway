namespace IPS.Middleware.Infrastructure.Proxy;

// The error codes of Annex E (p. 67-68), used when a reject carries a code but no description.
internal static class ProxyErrorCodes
{
    internal const string InternalError = "MS03";

    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        ["BE18"] = "Account Holder / requested alias not found",
        ["BE15"] = "Stored participant code doesn't match request code",
        ["AM05"] = "Duplicate alias",
        ["AC01"] = "Requested account not found or invalid account number",
        ["AT07"] = "Invalid alias",
        ["AM06"] = "Duplicate reference",
        ["FF01"] = "Generic validation error (invalid currency, duplicate sub-item, alias type, XML, or signature)",
        ["MTCH"] = "Verification of Payee - Match",
        ["CMTC"] = "Verification of Payee - Close Match",
        ["NMTC"] = "Verification of Payee - No Match",
        ["NOAP"] = "Verification of Payee - Not Possible",
        [InternalError] = "Internal error",
        ["RC01"] = "Invalid Sender"
    };

    internal static string Describe(string? code) => code switch
    {
        null => "Unknown error.",
        _ => Descriptions.TryGetValue(code, out var description) ? description : $"Unrecognized Proxy error code '{code}'."
    };
}
