using IPS.Middleware.Application.Payments.Camt056;

namespace IPS.Middleware.IntegrationTests.Payments;

internal static class Camt056Fixture
{
    internal static Camt056Request Request(string reference = "processing") => new()
    {
        ClientReference = reference,
        Id = "RCL-" + reference,
        RecallId = "CXL-" + reference,
        OriginalMessageId = "ORIG-MSG-1",
        OriginalEndToEndId = "ORIG-E2E-1",
        OriginalTransactionId = "ORIG-TX-1",
        OriginalCurrency = "GEL",
        OriginalAmount = 100.5m,
        OriginalSettlementDate = new DateOnly(2026, 10, 3),
        ReasonCode = "DUPL",
        OriginalTransaction = new()
        {
            SettlementDate = new DateOnly(2026, 10, 3),
            Debtor = new() { Name = "Original Payer", Account = "GE29NB0000000101904917" },
            DebtorAgent = new() { Bic = "BAGAGE22" },
            CreditorAgent = new() { Bic = "TBCBGE22" },
            Creditor = new() { Name = "Original Payee", Account = "GE95TB0000000123456789" }
        }
    };
}
