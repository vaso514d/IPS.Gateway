namespace IPS.Middleware.Application.Inbound.Composition;

public interface IIncomingCompositionRepository
{
    Task<IncomingReceiptState?> ReadAsync(Guid journalId, CancellationToken token);
    Task<bool> StageFirstReplyReadyAsync(Guid journalId, DateTimeOffset now, CancellationToken token);
}
