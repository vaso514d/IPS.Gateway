namespace IPS.Middleware.Application.Payments;

public static class PaymentMessageTypes
{
    public const string Pacs008 = "pacs.008";
    public const string Pacs008Definition = "pacs.008.001.12";
    public const string Pacs009 = "pacs.009";
    public const string Pacs009Definition = "pacs.009.001.11";
    public const string Pacs004 = "pacs.004";
    public const string Pacs004Definition = "pacs.004.001.13";
    public const string Pacs002 = "pacs.002";
    public const string Pacs002Definition = "pacs.002.001.14";

    // The message types this participant sends and tracks as outgoing payments.
    public static readonly IReadOnlyList<string> Outgoing = [Pacs008, Pacs009, Pacs004];

    // Inbound receipts name a message either by its short type or by its full message definition.
    public static bool IsPacs008(string messageType) => messageType is Pacs008 or Pacs008Definition;

    public static bool IsPacs009(string messageType) => messageType is Pacs009 or Pacs009Definition;

    public static bool IsPacs002(string messageType) => messageType is Pacs002 or Pacs002Definition;

    public static bool IsOutgoing(string messageType) => Outgoing.Contains(messageType);

    // Only pacs.008 has a status investigation (pacs.028); every other outgoing type is resent as a possible duplicate.
    public static bool HasInvestigation(string messageType) => messageType == Pacs008;

    public static string DefinitionOf(string messageType) => messageType switch
    {
        Pacs008 => Pacs008Definition,
        Pacs009 => Pacs009Definition,
        Pacs004 => Pacs004Definition,
        _ => throw new ArgumentOutOfRangeException(nameof(messageType), messageType, "Not an outgoing message type.")
    };
}
