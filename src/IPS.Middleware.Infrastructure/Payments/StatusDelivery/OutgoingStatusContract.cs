using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.MiidleWear.Contracts.Transactions;
using DomainStatus = IPS.Middleware.Domain.Transactions.TransactionStatus;

namespace IPS.Middleware.Infrastructure.Payments.StatusDelivery;

public static class OutgoingStatusContract
{
    public static TransactionStatusDto Map(OutgoingStatus status) => new()
    {
        TransactionId = status.PaymentId,
        ClientReference = status.ClientReference,
        Direction = TransactionDirection.Outgoing,
        MessageKind = status.MessageType switch
        {
            "pacs.008" => IpsMessageKind.Pacs008,
            "pacs.009" => IpsMessageKind.Pacs009,
            "pacs.004" => IpsMessageKind.Pacs004,
            "camt.056" => IpsMessageKind.Camt056,
            "camt.029" => IpsMessageKind.Camt029,
            "pain.002" => IpsMessageKind.Pain002,
            _ => throw new NotSupportedException("Unsupported outgoing message kind.")
        },
        Status = status.Status switch
        {
            DomainStatus.Accepted => TransactionStatus.Accepted,
            DomainStatus.Rejected => TransactionStatus.Rejected,
            DomainStatus.NotSent => TransactionStatus.NotSent,
            DomainStatus.ManualReview => TransactionStatus.ManualReview,
            DomainStatus.ManuallyResolved => TransactionStatus.ManuallyResolved,
            _ => TransactionStatus.Processing
        },
        StatusAtUtc = status.StatusAtUtc,
        ReasonCode = status.Details.ReasonCode,
        IpsInternalCode = status.Details.IpsInternalCode,
        Description = status.IsReportable ? status.Details.Description : null,
        MessageId = status.MessageId,
        EndToEndId = status.EndToEndId
    };
}
