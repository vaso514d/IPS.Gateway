using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pain002;
using IPS.MiidleWear.Contracts.Proxy;

namespace IPS.Middleware.AspireTests;

// Valid requests for the sending side of the service, with a client reference per call so that tests never collide.
internal static class Requests
{
    internal const string Pain002Send = "/api/ips/pain002/send";
    internal const string Pacs008Send = "/api/ips/pacs008/send";

    internal static string Reference(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..12];

    internal static Pain002PaymentStatusReportDto Pain002(string reference) => new()
    {
        ClientReference = reference,
        Id = "ID-" + reference,
        OriginalMessageId = "PAIN001-MSG-1",
        OriginalPaymentInformationId = "PMTINF-" + reference,
        ReasonCode = "CUST"
    };

    internal static Pacs008InstantPaymentRequestDto Pacs008(string reference)
    {
        var now = DateTimeOffset.UtcNow;
        return new()
        {
            ClientReference = reference,
            InstructionId = "BANK-" + reference,
            EndToEndId = "E2E-" + reference,
            CreationDateTime = now.AddMilliseconds(-500),
            AcceptanceDateTime = now,
            Amount = 12.34m,
            Currency = "GEL",
            InstructionPriority = "HIGH",
            Debtor = new() { Type = 1, Name = "Debtor", Account = "GE95TB0000000123456789" },
            Creditor = new() { Type = 1, Name = "Creditor", ParticipantBic = "TBCBGE22", Account = "GE29NB0000000101904917" }
        };
    }

    internal static ProxyRegisterRequestDto ProxyRegister() => new(
        new ProxyAccountHolderDto("01001011111", "Individual", "Given", "Surname", null, false, "GE", "GE", null, null, null, null),
        new ProxyAccountDto("GE29NB0000000101904917", true, "GEL", "CACC", null, null),
        [new ProxyIdentifierDto("MBNO", "+995599123456")],
        null,
        null);

    internal static ProxyUpdateRequestDto ProxyUpdate() =>
        new("01001011111", "Individual", null, null, null, null, [new ProxyIdentifierDto("MBNO", "+995599000000")], null, null);

    internal static ProxyRemoveRequestDto ProxyRemove() =>
        new("01001011111", "Individual", "GE29NB0000000101904917", "GEL", false, null, null, null);
}
