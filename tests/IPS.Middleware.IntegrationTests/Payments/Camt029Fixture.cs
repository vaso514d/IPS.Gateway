using IPS.Middleware.Application.Payments.Camt029;
using IPS.Middleware.Application.Payments.Recalls;

namespace IPS.Middleware.IntegrationTests.Payments;

internal static class Camt029Fixture
{
    // The answer comes from the creditor's participant, which is us: the creditor agent is our BIC.
    internal static Camt029Request Request(string reference = "processing") => new()
    {
        ClientReference = reference,
        Id = "ANS-" + reference,
        CancellationStatusId = "CST-" + reference,
        OriginalMessageId = "RCL-MSG-1",
        OriginalEndToEndId = "ORIG-E2E-1",
        OriginalTransactionId = "ORIG-TX-1",
        ReasonCode = "CUST",
        OriginalTransaction = new Camt029OriginalInput
        {
            Currency = "GEL",
            Amount = 100.5m,
            SettlementDate = new DateOnly(2026, 10, 3),
            Debtor = new() { Name = "Original Payer", Account = "GE29NB0000000101904917" },
            DebtorAgent = new() { Bic = "TBCBGE22" },
            CreditorAgent = new() { Bic = "BAGAGE22" },
            Creditor = new() { Name = "Original Payee", Account = "GE95TB0000000123456789" }
        }
    };
}
