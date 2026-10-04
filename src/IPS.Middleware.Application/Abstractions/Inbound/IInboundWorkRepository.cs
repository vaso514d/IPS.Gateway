using IPS.Middleware.Application.Inbound;

namespace IPS.Middleware.Application.Abstractions.Inbound;

public interface IInboundWorkRepository
{
    Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken);
    Task<InboundClaim?> StageClaimAsync(Guid journalId, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken);
    Task<bool> StageFinishAsync(InboundClaim claim, DateTimeOffset now, DateTimeOffset? nextActionAtUtc, CancellationToken cancellationToken);
}
