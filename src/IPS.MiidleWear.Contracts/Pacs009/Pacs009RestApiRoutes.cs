namespace IPS.MiidleWear.Contracts.Pacs009;

public static class Pacs009RestApiRoutes
{
    /// <summary>
    /// Implemented by the gateway; called by external clients to initiate outbound IPS pacs.009.
    /// </summary>
    public const string Send = "/api/v1/ips/pacs009/send";
}
