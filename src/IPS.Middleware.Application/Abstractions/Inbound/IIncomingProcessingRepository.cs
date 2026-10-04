using IPS.Middleware.Application.Inbound;
using IPS.Middleware.Application.Inbound.Pacs008;

namespace IPS.Middleware.Application.Abstractions.Inbound;

public interface IIncomingProcessingRepository
{
    Task<IncomingProcessingSnapshot?> ReadAsync(Guid paymentId, CancellationToken cancellationToken);
    Task<bool> IsOwnerAsync(IncomingPaymentClaim claim, DateTimeOffset now, CancellationToken cancellationToken);
    Task<IncomingCoreCall> StageCallAsync(IncomingPaymentClaim claim, CoreCallKind kind, DateTimeOffset now, CancellationToken cancellationToken);
    Task StageCompletionAsync(IncomingPaymentClaim claim, Guid callId, CoreCallCompletion completion, DateTimeOffset now, CancellationToken cancellationToken);
    Task StageConsumptionAsync(IncomingPaymentClaim claim, Guid callId, DateTimeOffset now, CancellationToken cancellationToken);
    Task StageFinishAsync(IncomingPaymentClaim claim, DateTimeOffset now, DateTimeOffset? followUpAtUtc, CancellationToken cancellationToken);
}
