using IPS.Middleware.Application.Inbound.Processing;

namespace IPS.Middleware.Application.Inbound.Composition;

/// <summary>Each asynchronous operation owns a fresh scope; failed units of work never cross phases.</summary>
public interface IIncomingWorkflowExecution
{
    Task<IncomingReceiptState?> ReadAsync(Guid journalId, CancellationToken token);
    Task<IncomingCompositionResult> PrepareAsync(Guid journalId, CancellationToken token);
    Task<IncomingProcessingResult?> ProcessPaymentAsync(Guid paymentId, CancellationToken token);
    Task<bool> MakeFirstReplyReadyAsync(Guid journalId, CancellationToken token);
    Task DeliverReplyAsync(Guid journalId, CancellationToken token);
    bool TryNotifyReply(Guid journalId);
}
