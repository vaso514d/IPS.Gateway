using IPS.Middleware.Application.Payments.Investigation;

namespace IPS.Middleware.Infrastructure.Persistence.Outgoing;

internal sealed class InvestigationRow
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public int Number { get; set; }
    public string MessageId { get; set; } = "";
    public string StatusRequestId { get; set; } = "";
    public DateTimeOffset DeadlineUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? UnsignedXml { get; set; }
    public InvestigationOutcome? Outcome { get; set; }
    public string? DetailsJson { get; set; }
    public string? TransportFailure { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
