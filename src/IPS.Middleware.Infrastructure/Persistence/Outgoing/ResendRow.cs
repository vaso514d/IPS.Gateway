using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Persistence.Outgoing;

internal sealed class ResendRow
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public int Number { get; set; }
    public Guid InvestigationId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public IpsReplyStatus? Outcome { get; set; }
    public string? DetailsJson { get; set; }
    public string? TransportFailure { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
