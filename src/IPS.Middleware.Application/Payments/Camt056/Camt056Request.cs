using IPS.Middleware.Application.Payments.Recalls;

namespace IPS.Middleware.Application.Payments.Camt056;

// A camt.056 request as the caller supplied it. Every field is optional here; validation reports what is missing.
public sealed record Camt056Request : IOutgoingPaymentRequest
{
    public string? ClientReference { get; init; }
    public string? Id { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? RecallId { get; init; }
    public string? OriginalMessageId { get; init; }
    public string? OriginalEndToEndId { get; init; }
    public string? OriginalTransactionId { get; init; }
    public string? OriginalCurrency { get; init; }
    public decimal? OriginalAmount { get; init; }
    public DateOnly? OriginalSettlementDate { get; init; }
    public string? ReasonCode { get; init; }
    public RecallOriginalInput? OriginalTransaction { get; init; }
}
