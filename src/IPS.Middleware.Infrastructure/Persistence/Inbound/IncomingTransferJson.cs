using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Application.Payments;

namespace IPS.Middleware.Infrastructure.Persistence.Inbound;

// The frozen content of a received transfer, stored under its kind: the kind selects the concrete type on the way back.
internal static class IncomingTransferJson
{
    internal static string Write(IIncomingTransferContent content) => content switch
    {
        IncomingPacs009 pacs009 => IncomingPaymentJson.Write(pacs009),
        IncomingPacs004 pacs004 => IncomingPaymentJson.Write(pacs004),
        _ => throw new ArgumentOutOfRangeException(nameof(content), content.GetType().Name, "No stored form for this transfer content.")
    };

    internal static IIncomingTransferContent Read(string kind, string json) => kind switch
    {
        PaymentMessageTypes.Pacs009 => IncomingPaymentJson.Read<IncomingPacs009>(json),
        PaymentMessageTypes.Pacs004 => IncomingPaymentJson.Read<IncomingPacs004>(json),
        _ => throw new NotSupportedException($"Incoming transfer kind {kind} is not supported.")
    };
}
