using IPS.Middleware.Application.Payments.Pain002;
using IPS.MiidleWear.Contracts.Pain002;

namespace IPS.Middleware.Api.Payments;

internal static class Pain002RequestMapping
{
    public static Pain002Request Map(Pain002PaymentStatusReportDto report) => new()
    {
        ClientReference = report.ClientReference,
        Id = report.Id,
        CreatedAt = report.CreDtTm,
        OriginalMessageId = report.OriginalMessageId,
        OriginalPaymentInformationId = report.OriginalPaymentInformationId,
        ReasonCode = report.ReasonCode,
        AdditionalInformation = report.AdditionalInformation,
        OriginatorName = report.OriginatorName
    };
}
