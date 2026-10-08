namespace IPS.Middleware.Application.Payments.Pain002;

// A pain.002 request as the caller supplied it. Every field is optional here; validation reports what is missing.
public sealed record Pain002Request : IOutgoingPaymentRequest
{
    public string? ClientReference { get; init; }
    public string? Id { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? OriginalMessageId { get; init; }
    public string? OriginalPaymentInformationId { get; init; }
    public string? ReasonCode { get; init; }
    public string? AdditionalInformation { get; init; }
    public string? OriginatorName { get; init; }
}
