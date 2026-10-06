using IPS.Middleware.Application.Payments.Recalls;

namespace IPS.Middleware.Application.Payments.Camt029;

// A camt.029 request as the caller supplied it. Every field is optional here; validation reports what is missing.
public sealed record Camt029Request : IOutgoingPaymentRequest
{
    public string? ClientReference { get; init; }
    public string? Id { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? CancellationStatusId { get; init; }
    public string? OriginalMessageId { get; init; }
    public string? OriginalEndToEndId { get; init; }
    public string? OriginalTransactionId { get; init; }
    public string? ReasonCode { get; init; }
    public string? AdditionalInformation { get; init; }
    public Camt029OriginalInput? OriginalTransaction { get; init; }
}

// A camt.029 also quotes the amount of the recalled payment.
public sealed record Camt029OriginalInput : RecallOriginalInput
{
    public string? Currency { get; init; }
    public decimal? Amount { get; init; }
}
