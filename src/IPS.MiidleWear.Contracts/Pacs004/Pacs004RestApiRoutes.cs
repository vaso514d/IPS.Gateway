namespace IPS.MiidleWear.Contracts.Pacs004;

public static class Pacs004RestApiRoutes
{
    /// <summary>
    /// Implemented by the external client/core side; called by the gateway for inbound IPS pacs.004.
    /// </summary>
    public const string Receive = "/api/ips/pacs004/receive";

    /// <summary>
    /// Implemented by the gateway; called by external clients to initiate outbound IPS pacs.004.
    /// </summary>
    public const string Send = "/api/ips/pacs004/send";
}
