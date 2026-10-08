using IPS.Middleware.Application.Payments.Pacs004;

namespace IPS.Middleware.IntegrationTests.Payments;

internal static class Pacs004Fixture
{
    internal static Pacs004Request Request(string reference = "processing") => new()
    {
        ClientReference = reference,
        Id = "RTR-" + reference,
        Amount = 100.5m,
        Currency = "GEL",
        ValueDate = new DateOnly(2026, 10, 4),
        InstructedAgent = "TBCBGE22",
        Debtor = new() { Name = "Original Payer", Account = "GE95TB0000000123456789" },
        Creditor = new() { Name = "Original Payee", Account = "GE29NB0000000101904917" },
        Original = new()
        {
            TransactionId = "ORIG-TX-1",
            EndToEndId = "ORIG-E2E-1",
            ValueDate = new DateOnly(2026, 10, 3)
        }
    };
}
