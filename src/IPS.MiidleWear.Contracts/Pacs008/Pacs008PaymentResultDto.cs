using System.Text.Json.Serialization;

namespace IPS.MiidleWear.Contracts.Pacs008;

/// <summary>
/// Final synchronous result for a pacs.008 request. This result is used by the gateway to build pacs.002.
/// </summary>
public sealed record Pacs008PaymentResultDto
{
    [JsonPropertyName("Id")]
    public string? Id { get; init; }

    [JsonPropertyName("EndToEndId")]
    public string? EndToEndId { get; init; }

    [JsonPropertyName("TxId")]
    public string? TxId { get; init; }

    [JsonPropertyName("Status")]
    public string Status { get; init; } = Pacs008PaymentStatuses.Accepted;

    [JsonPropertyName("Accepted")]
    public bool Accepted => Status.Equals(Pacs008PaymentStatuses.Accepted, StringComparison.OrdinalIgnoreCase);

    [JsonPropertyName("ReasonCode")]
    public string? ReasonCode { get; init; }

    [JsonPropertyName("InternalErrorCode")]
    public int? InternalErrorCode { get; init; }

    [JsonPropertyName("CoreReference")]
    public string? CoreReference { get; init; }

    [JsonPropertyName("Description")]
    public string? Description { get; init; }

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
}

public static class Pacs008PaymentStatuses
{
    public const string Accepted = "ACCP";

    public const string Rejected = "RJCT";
}
