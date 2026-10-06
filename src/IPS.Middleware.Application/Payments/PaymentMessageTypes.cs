namespace IPS.Middleware.Application.Payments;

public static class PaymentMessageTypes
{
    public const string Pacs008 = "pacs.008";
    public const string Pacs008Definition = "pacs.008.001.12";
    public const string Pacs002 = "pacs.002";
    public const string Pacs002Definition = "pacs.002.001.14";

    // Inbound receipts name a message either by its short type or by its full message definition.
    public static bool IsPacs008(string messageType) => messageType is Pacs008 or Pacs008Definition;

    public static bool IsPacs002(string messageType) => messageType is Pacs002 or Pacs002Definition;
}
