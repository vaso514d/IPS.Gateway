using IPS.Middleware.Application.Payments.StatusDelivery;

namespace IPS.Middleware.Infrastructure.Persistence.Outgoing;

internal sealed class OutgoingStatusDeliveryRow
{
    public Guid PaymentId { get; set; }
    public int Sequence { get; set; }
    public int PayloadVersion { get; set; } = 1;
    public string PayloadJson { get; set; } = "";
    public StatusDeliveryState State { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAtUtc { get; set; }
    public Guid? ClaimToken { get; set; }
    public DateTimeOffset? ClaimExpiresAtUtc { get; set; }
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public string? LastFailure { get; set; }
    public byte[] RowVersion { get; set; } = [];

    internal OutgoingStatus Payload() => PayloadVersion == 1
        ? PaymentJson.Read<OutgoingStatus>(PayloadJson)!
        : throw new NotSupportedException($"Unsupported outgoing status payload version {PayloadVersion}.");
}
