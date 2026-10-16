using System.Text.Json.Serialization;

namespace IPS.MiidleWear.Contracts.Pacs008;

/// <summary>
/// Final synchronous result for a pacs.008 request. This result is used by the gateway to build pacs.002.
/// </summary>
public sealed record Pacs008PaymentResultDto
{
    /// <summary>Omit it, or echo the key of the delivered message exactly: pacs.009 id, pacs.004 return id, pain.001 paymentInformationId, camt.056 and camt.029 id, camt.055 msgId. Leave it empty for a pacs.008, which carries no message id. Any other value makes the answer unusable.</summary>
    [JsonPropertyName("Id")]
    public string? Id { get; init; }

    /// <summary>Omit it, or echo the delivered endToEndId exactly (pacs.008, pacs.009). Not checked for the other kinds.</summary>
    [JsonPropertyName("EndToEndId")]
    public string? EndToEndId { get; init; }

    /// <summary>Omit it, or echo the delivered txId exactly (pacs.009). Leave it empty for a pacs.008, which carries no transaction id. Not checked for the other kinds.</summary>
    [JsonPropertyName("TxId")]
    public string? TxId { get; init; }

    /// <summary>ACCP (accepted), RJCT (refused, with ReasonCode) or, only in the payment-status query, PDNG (still processing); see Pacs008PaymentStatuses. Read case-insensitively.</summary>
    [JsonPropertyName("Status")]
    public string Status { get; init; } = Pacs008PaymentStatuses.Accepted;

    /// <summary>True when Status is ACCP. Informational: the gateway reads Status.</summary>
    [JsonPropertyName("Accepted")]
    public bool Accepted => Status.Equals(Pacs008PaymentStatuses.Accepted, StringComparison.OrdinalIgnoreCase);

    /// <summary>The ISO 20022 reason of an RJCT, for example AC04 or AM04. For an incoming pacs.008 it is the reason given to IPS in the pacs.002.</summary>
    [JsonPropertyName("ReasonCode")]
    public string? ReasonCode { get; init; }

    /// <summary>The core system's own error number, stored with the gateway's record of the answer.</summary>
    [JsonPropertyName("InternalErrorCode")]
    public int? InternalErrorCode { get; init; }

    /// <summary>The core system's reference of the posting, stored with the gateway's record of the answer.</summary>
    [JsonPropertyName("CoreReference")]
    public string? CoreReference { get; init; }

    /// <summary>Free text, stored with the gateway's record of the answer.</summary>
    [JsonPropertyName("Description")]
    public string? Description { get; init; }

    /// <summary>When the core system decided; the gateway records it as the time of the answer.</summary>
    [JsonPropertyName("ProcessedAtUtc")]
    public DateTimeOffset ProcessedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public static Pacs008PaymentResultDto Accept(
        string? id,
        string? endToEndId,
        string? txId,
        string? coreReference = null,
        string? description = null)
    {
        return new Pacs008PaymentResultDto
        {
            Id = id,
            EndToEndId = endToEndId,
            TxId = txId,
            Status = Pacs008PaymentStatuses.Accepted,
            CoreReference = coreReference,
            Description = description
        };
    }

    public static Pacs008PaymentResultDto Reject(
        string? id,
        string? endToEndId,
        string? txId,
        string reasonCode,
        int? internalErrorCode = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new ArgumentException("Reject reason code is required.", nameof(reasonCode));
        }

        return new Pacs008PaymentResultDto
        {
            Id = id,
            EndToEndId = endToEndId,
            TxId = txId,
            Status = Pacs008PaymentStatuses.Rejected,
            ReasonCode = reasonCode.Trim().ToUpperInvariant(),
            InternalErrorCode = internalErrorCode,
            Description = description
        };
    }

    /// <summary>
    /// The answer to the payment-status query while the core system is still processing the payment. The gateway
    /// treats it as not yet final and asks again later. As the answer to a receive call it counts as no answer: for an
    /// incoming pacs.008 the IPS deadline can then pass, and IPS is told the payment is rejected.
    /// </summary>
    public static Pacs008PaymentResultDto Pending(
        string? id,
        string? endToEndId,
        string? txId,
        string? description = null)
    {
        return new Pacs008PaymentResultDto
        {
            Id = id,
            EndToEndId = endToEndId,
            TxId = txId,
            Status = Pacs008PaymentStatuses.Pending,
            Description = description
        };
    }
}

public static class Pacs008PaymentStatuses
{
    public const string Accepted = "ACCP";

    public const string Rejected = "RJCT";

    /// <summary>Still processing; only an answer to the payment-status query. The gateway asks again later.</summary>
    public const string Pending = "PDNG";
}
