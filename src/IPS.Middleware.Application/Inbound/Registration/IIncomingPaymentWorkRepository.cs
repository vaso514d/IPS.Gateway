namespace IPS.Middleware.Application.Inbound.Registration;

public interface IIncomingPaymentWorkRepository
{
    Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken);
    Task<IncomingPaymentClaim?> StageClaimAsync(Guid paymentId, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken);
    Task<bool> StageReleaseAsync(
        IncomingPaymentClaim claim,
        DateTimeOffset now,
        DateTimeOffset nextActionAtUtc,
        CancellationToken cancellationToken);
}
