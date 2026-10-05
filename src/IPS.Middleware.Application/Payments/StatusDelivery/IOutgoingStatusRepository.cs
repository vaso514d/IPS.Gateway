namespace IPS.Middleware.Application.Payments.StatusDelivery;

public interface IOutgoingStatusRepository
{
    Task<OutgoingStatus?> ReadAsync(string reference, CancellationToken cancellationToken);
    Task<IReadOnlyList<StatusDeliveryKey>> FindDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken);
    Task<StatusDeliveryWork?> ReadWorkAsync(StatusDeliveryKey key, CancellationToken cancellationToken);
    Task<Guid?> StageClaimAsync(StatusDeliveryKey key, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken);
    Task<bool> StageFinishAsync(StatusDeliveryKey key, Guid token, DateTimeOffset now, StatusDeliveryRetry result,
        string? failure, bool expired, CancellationToken cancellationToken);
    Task<bool> StageAcknowledgeAsync(OutgoingStatus observed, DateTimeOffset now, CancellationToken cancellationToken);
}
