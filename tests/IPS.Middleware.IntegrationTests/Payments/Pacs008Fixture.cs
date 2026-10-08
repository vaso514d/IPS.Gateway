using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.IntegrationTests.Payments;

internal static class Pacs008Fixture
{
    internal static readonly DateTimeOffset Created = new(2026, 10, 3, 23, 59, 59, TimeSpan.Zero);
    internal static readonly Pacs008Policy Policy = new("BAGAGE22", "TRESGE22", [new("GEL")], ["MEMBER-1"]);

    internal static Pacs008Request Request() => new()
    {
        ClientReference = "never-on-wire",
        InstructionId = "BANK-1",
        EndToEndId = "E2E-1",
        CreationDateTime = Created,
        AcceptanceDateTime = Created.AddMilliseconds(500),
        Amount = 12.34567m,
        Currency = "GEL",
        InstructionPriority = "HIGH",
        Debtor = new() { Type = 1, Name = "ქართული & Debtor", Account = "GE95TB0000000123456789" },
        Creditor = new() { Type = 1, Name = "Creditor", ParticipantBic = "TBCBGE22", Account = "GE29NB0000000101904917" }
    };

}
