using IPS.Middleware.Application.Inbound.Pacs008;

namespace IPS.Middleware.Infrastructure.Persistence.Inbound;

internal sealed class IncomingCoreCallRow
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public int Number { get; set; }
    public CoreCallKind Kind { get; set; }
    public Guid OwnerToken { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public string? CompletionJson { get; set; }
    public bool Consumed { get; set; }
    public IncomingCoreCall Snapshot() => new(Id, Number, Kind, OwnerToken, StartedAtUtc,
        CompletionJson is null ? null : IncomingPaymentJson.Read<CoreCallCompletion>(CompletionJson), Consumed);
}
