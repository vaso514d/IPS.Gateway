using System.Text.Json.Serialization;

namespace IPS.MiidleWear.Contracts.Transactions;

/// <summary>
/// The gateway's view of one transaction, keyed by the core system's <see cref="ClientReference"/>. Returned when a
/// send is accepted (202, status Processing), by the status query (GET), and posted to the core system when the
/// status becomes final.
/// </summary>
public sealed record TransactionStatusDto
{
    [JsonPropertyName("messageKind")]
    public IpsMessageKind MessageKind { get; init; }

    /// <summary>The core system's unique id for the transaction (unique across every message type).</summary>
    [JsonPropertyName("clientReference")]
    public string ClientReference { get; init; } = string.Empty;

    /// <summary>Outgoing (identified by <see cref="ClientReference"/>) or incoming (identified by <see cref="CoreReference"/>).</summary>
    [JsonPropertyName("direction")]
    public TransactionDirection Direction { get; init; } = TransactionDirection.Outgoing;

    /// <summary>
    /// Incoming only: the key the core system knows the payment by — the Idempotency-Key of the receive call
    /// (pacs.008/009: EndToEndId, pacs.004: the return's Id). Empty on outgoing transactions.
    /// </summary>
    [JsonPropertyName("coreReference")]
    public string? CoreReference { get; init; }

    /// <summary>The gateway's id for the transaction.</summary>
    [JsonPropertyName("transactionId")]
    public Guid TransactionId { get; init; }

    [JsonPropertyName("status")]
    public TransactionStatus Status { get; init; }

    /// <summary>When the transaction entered <see cref="Status"/>.</summary>
    [JsonPropertyName("statusAtUtc")]
    public DateTimeOffset StatusAtUtc { get; init; }

    /// <summary>ISO 20022 reason code of the status (e.g. AC01, TM01); set on rejections.</summary>
    [JsonPropertyName("reasonCode")]
    public string? ReasonCode { get; init; }

    /// <summary>IPS internal error code (Annex D, Annex 2 — e.g. 1009); set when IPS supplied one.</summary>
    [JsonPropertyName("ipsInternalCode")]
    public int? IpsInternalCode { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>GrpHdr/MsgId (or the message's own id) sent to IPS.</summary>
    [JsonPropertyName("messageId")]
    public string? MessageId { get; init; }

    [JsonPropertyName("endToEndId")]
    public string? EndToEndId { get; init; }
}

/// <summary>
/// GET query the gateway sends to the core system (<see cref="Abstractions.IClientPaymentReceiver.GetPaymentStatusAsync"/>):
/// what happened to an incoming payment whose receive call got no answer.
/// </summary>
public sealed record InboundPaymentStatusQueryDto
{
    /// <summary>Pacs008, Pacs009 or Pacs004.</summary>
    [JsonPropertyName("messageKind")]
    public IpsMessageKind? MessageKind { get; init; }

    /// <summary>The Idempotency-Key of the receive call (pacs.008/009: EndToEndId, pacs.004: the return's Id).</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; init; }
}

/// <summary>GET query: the status of one transaction.</summary>
public sealed record TransactionStatusQueryDto
{
    /// <summary>The message kind the transaction was sent as.</summary>
    [JsonPropertyName("messageKind")]
    public IpsMessageKind? MessageKind { get; init; }

    /// <summary>The core system's unique id for the transaction.</summary>
    [JsonPropertyName("clientReference")]
    public string? ClientReference { get; init; }
}

public static class TransactionRestApiRoutes
{
    /// <summary>Implemented by the gateway: the core system reads a transaction's current status.</summary>
    public const string Status = "/api/ips/transactions/status";

    /// <summary>Implemented by the core system: the gateway posts a transaction's final status.</summary>
    public const string ReceiveStatus = "/api/ips/transactions/status/receive";

    /// <summary>Implemented by the core system: the gateway reads the core's outcome of an incoming payment.</summary>
    public const string PaymentStatus = "/api/ips/payments/status";
}
