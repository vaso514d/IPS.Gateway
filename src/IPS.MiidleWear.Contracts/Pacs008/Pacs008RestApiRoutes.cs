namespace IPS.MiidleWear.Contracts.Pacs008;

public static class Pacs008RestApiRoutes
{
    /// <summary>
    /// Implemented by the external client/core side; called by the gateway for inbound IPS pacs.008.
    /// </summary>
    public const string Receive = "/api/ips/pacs008/receive";

    /// <summary>
    /// Implemented by the gateway; called by external clients to initiate outbound IPS pacs.008.
    /// </summary>
    public const string Send = "/api/ips/pacs008/send";
}
