namespace IPS.MiidleWear.Contracts.Proxy;

public static class ProxyRestApiRoutes
{
    /// <summary>Implemented by the gateway; registers an account holder, account and proxy identifiers (Annex E §1.2.1).</summary>
    public const string Register = "/api/v1/proxy/register";

    /// <summary>Implemented by the gateway; updates registered data (Annex E §1.2.2).</summary>
    public const string Update = "/api/v1/proxy/update";

    /// <summary>Implemented by the gateway; removes an account, proxies, authorized persons or beneficial owners (Annex E §1.2.2).</summary>
    public const string Remove = "/api/v1/proxy/remove";
}
