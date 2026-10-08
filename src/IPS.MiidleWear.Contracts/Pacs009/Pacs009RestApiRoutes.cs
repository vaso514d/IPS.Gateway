namespace IPS.MiidleWear.Contracts.Pacs009;

public static class Pacs009RestApiRoutes
{
    /// <summary>
    /// Implemented by the external client/core side; called by the gateway for inbound IPS pacs.009.
    /// </summary>
    public const string Receive = "/api/ips/pacs009/receive";

    /// <summary>
    /// Implemented by the gateway; called by external clients to initiate outbound IPS pacs.009.
    /// </summary>
    public const string Send = "/api/ips/pacs009/send";
}
