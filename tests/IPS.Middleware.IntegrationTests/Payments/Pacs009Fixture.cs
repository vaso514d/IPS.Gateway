using IPS.Middleware.Application.Payments.Pacs009;

namespace IPS.Middleware.IntegrationTests.Payments;

internal static class Pacs009Fixture
{
    internal static Pacs009Request Request(string reference = "processing") => new()
    {
        ClientReference = reference,
        Id = "P9-" + reference,
        DebtorAgent = new() { Bic = "BAGAGE22" },
        CreditorAgent = new() { Bic = "TBCBGE22" },
        EndToEndId = "E2E-009",
        ValueDate = new DateOnly(2026, 10, 4),
        Currency = "GEL",
        Amount = 100.5m,
        Purpose = "INTC",
        AdditionalPurpose = "Liquidity transfer",
        DebtorAccount = "GE29NB0000000101904917",
        CreditorAccount = "GE95TB0000000123456789"
    };
}
