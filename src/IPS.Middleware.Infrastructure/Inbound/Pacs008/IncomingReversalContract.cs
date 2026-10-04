using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.MiidleWear.Contracts.Transactions;

namespace IPS.Middleware.Infrastructure.Inbound.Pacs008;

public static class IncomingReversalContract
{
    public static TransactionStatusDto Map(ReversalNotification notification) => new()
    {
        MessageKind = IpsMessageKind.Pacs008,
        ClientReference = string.Empty,
        Direction = TransactionDirection.Incoming,
        CoreReference = notification.EndToEndId,
        TransactionId = notification.PaymentId,
        Status = TransactionStatus.Rejected,
        StatusAtUtc = notification.StatusAtUtc,
        ReasonCode = notification.ReasonCode,
        Description = notification.Description,
        MessageId = notification.GroupMessageId,
        EndToEndId = notification.EndToEndId
    };
}
