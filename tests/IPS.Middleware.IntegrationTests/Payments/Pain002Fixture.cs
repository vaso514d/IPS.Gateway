using IPS.Middleware.Application.Payments.Pain002;

namespace IPS.Middleware.IntegrationTests.Payments;

internal static class Pain002Fixture
{
    internal static Pain002Request Request(string reference = "processing") => new()
    {
        ClientReference = reference,
        Id = "REF-" + reference,
        OriginalMessageId = "PAIN001-MSG-1",
        OriginalPaymentInformationId = "PMTINF-1",
        ReasonCode = "CUST"
    };
}
