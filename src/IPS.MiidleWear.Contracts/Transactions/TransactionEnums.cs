using System.Text.Json.Serialization;

namespace IPS.MiidleWear.Contracts.Transactions;

/// <summary>The kind of IPS message a transaction belongs to. Serialized as its name ("Pacs008", …).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IpsMessageKind>))]
public enum IpsMessageKind
{
    Pacs008 = 1,
    Pacs009 = 2,
    Pacs004 = 3,
    Camt056 = 4,
    Camt029 = 5,
    Pain002 = 6,
    Pain001 = 7,
    Camt055 = 8
}

/// <summary>
/// Transaction status as the core system sees it. The gateway's internal recovery states (sending, uncertain,
/// investigating, resending) are all reported as <see cref="Processing"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TransactionStatus>))]
public enum TransactionStatus
{
    /// <summary>Accepted by the gateway; the outcome at IPS is not final yet.</summary>
    Processing = 1,

    /// <summary>Final: IPS accepted the transaction.</summary>
    Accepted = 2,

    /// <summary>Final: IPS rejected the transaction (see the reason code).</summary>
    Rejected = 3,

    /// <summary>Final: the transaction never reached IPS (no connection could be established).</summary>
    NotSent = 4,

    /// <summary>The outcome could not be established automatically; an operator must resolve it.</summary>
    ManualReview = 5,

    /// <summary>Final: resolved by an operator after manual review.</summary>
    ManuallyResolved = 6
}

/// <summary>Who started the transaction: the core system (sent to IPS) or another participant (received from IPS).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TransactionDirection>))]
public enum TransactionDirection
{
    /// <summary>Sent by the core system to IPS; identified by clientReference.</summary>
    Outgoing = 1,

    /// <summary>Received from IPS and passed to the core system; identified by coreReference.</summary>
    Incoming = 2
}
