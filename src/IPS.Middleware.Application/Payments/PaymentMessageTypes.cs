namespace IPS.Middleware.Application.Payments;

public static class PaymentMessageTypes
{
    public const string Pacs008 = "pacs.008";
    public const string Pacs008Definition = "pacs.008.001.12";
    public const string Pacs009 = "pacs.009";
    public const string Pacs009Definition = "pacs.009.001.11";
    public const string Pacs004 = "pacs.004";
    public const string Pacs004Definition = "pacs.004.001.13";
    public const string Camt056 = "camt.056";
    public const string Camt056Definition = "camt.056.001.11";
    public const string Camt029 = "camt.029";
    public const string Camt029Definition = "camt.029.001.13";
    public const string Camt055 = "camt.055";
    public const string Camt055Definition = "camt.055.001.12";
    public const string Camt055Spelled012 = "camt.055.001.012";
    public const string Camt055Spelled08 = "camt.055.001.08";
    public const string Pain002 = "pain.002";
    public const string Pain002Definition = "pain.002.001.14";
    public const string Pain001 = "pain.001";
    public const string Pain001Definition = "pain.001.001.12";
    public const string Pacs002 = "pacs.002";
    public const string Pacs002Definition = "pacs.002.001.14";

    // The message types this participant sends and tracks as outgoing payments.
    public static readonly IReadOnlyList<string> Outgoing = [Pacs008, Pacs009, Pacs004, Camt056, Camt029, Pain002];

    // Inbound receipts name a message either by its short type or by its full message definition.
    public static bool IsPacs008(string messageType) => messageType is Pacs008 or Pacs008Definition;

    public static bool IsPacs004(string messageType) => messageType is Pacs004 or Pacs004Definition;

    public static bool IsPacs009(string messageType) => messageType is Pacs009 or Pacs009Definition;

    // The registry of the source also lists the definition as pain.001.001.012.
    public const string Pain001RegistryDefinition = "pain.001.001.012";

    public static bool IsPain001(string messageType) => messageType is Pain001 or Pain001Definition or Pain001RegistryDefinition;

    // A message that names its definition (the header of the message itself) rather than a short type.
    public static bool IsPain001Definition(string definition) => definition is Pain001Definition or Pain001RegistryDefinition;

    public static bool IsCamt056(string messageType) => messageType is Camt056 or Camt056Definition;

    public static bool IsCamt029(string messageType) => messageType is Camt029 or Camt029Definition;

    // The registry also spells the definition camt.055.001.012 and names the older camt.055.001.08; only version 12 is read.
    public static bool IsCamt055(string messageType) => messageType is Camt055 or Camt055Definition or Camt055Spelled012 or Camt055Spelled08;

    public static bool IsCamt055Definition(string definition) => definition is Camt055Definition or Camt055Spelled012;

    // Received from IPS and handed to the core system by the incoming transfer engine: a pacs.009, a return, a payment
    // initiation, a recall request, a refusal of our recall, or a cancellation request for a payment initiation.
    public static bool IsIncomingTransfer(string messageType) =>
        IsPacs009(messageType)
        || IsPacs004(messageType)
        || IsPain001(messageType)
        || IsCamt056(messageType)
        || IsCamt029(messageType)
        || IsCamt055(messageType);

    public static bool IsPacs002(string messageType) => messageType is Pacs002 or Pacs002Definition;

    public static bool IsOutgoing(string messageType) => Outgoing.Contains(messageType);

    // Only pacs.008 has a status investigation (pacs.028); every other outgoing type is resent as a possible duplicate.
    public static bool HasInvestigation(string messageType) => messageType == Pacs008;

    public static string DefinitionOf(string messageType) => messageType switch
    {
        Pacs008 => Pacs008Definition,
        Pacs009 => Pacs009Definition,
        Pacs004 => Pacs004Definition,
        Camt056 => Camt056Definition,
        Camt029 => Camt029Definition,
        Pain002 => Pain002Definition,
        _ => throw new ArgumentOutOfRangeException(nameof(messageType), messageType, "Not an outgoing message type.")
    };
}
