using IPS.Middleware.Application.Inbound.Registration;

namespace IPS.Middleware.Application.Inbound.Reconciliation;

public interface IIncomingReconciliationRepository
{
    Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken);
    Task<IncomingPaymentClaim?> StageClaimAsync(Guid paymentId, DateTimeOffset now, TimeSpan ownership, CancellationToken cancellationToken);
    Task StageReleaseAsync(
        IncomingPaymentClaim claim,
        DateTimeOffset now,
        DateTimeOffset? nextActionAtUtc,
        CancellationToken cancellationToken);
}
