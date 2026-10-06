using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pacs009;
using Xunit;

namespace IPS.Middleware.Tests.Payments;

public sealed class Pacs009ValidationTests
{
    private static readonly Pacs008Policy Policy = new("BAGAGE22", null, [new("GEL"), new("USD", Enabled: false)], ["INDRGE22"]);

    private static Pacs009Request Valid() => new()
    {
        ClientReference = "ref-1",
        Id = "PACS009-1",
        DebtorAgent = new() { Bic = "BAGAGE22" },
        CreditorAgent = new() { Bic = "TBCBGE22" },
        EndToEndId = "E2E-009",
        ValueDate = new DateOnly(2026, 10, 4),
        Currency = "GEL",
        Amount = 100.5m,
        DebtorAccount = "GE29NB0000000101904917",
        CreditorAccount = "GE95TB0000000123456789"
    };

    [Fact]
    public void A_valid_request_is_normalized_and_the_transaction_id_defaults_to_the_message_id()
    {
        var result = ValidatedPacs009.Validate(Valid() with
        {
            ClientReference = " ref-1 ",
            InstructionId = " ",
            Purpose = "INTC",
            CategoryPurpose = new() { Type = 1, Value = "Liquidity" },
            AdditionalPurpose = " Liquidity transfer "
        }, Policy);

        var payment = Assert.IsType<ValidatedPacs009>(result.Payment);
        Assert.Empty(result.Errors);
        Assert.Equal("ref-1", payment.ClientReference);
        Assert.Equal("PACS009-1", payment.MessageId);
        Assert.Equal("PACS009-1", payment.TransactionId);
        Assert.Equal("BAGAGE22", payment.ParticipantBic);
        Assert.Equal("GEL", payment.Currency);
        Assert.Null(payment.InstructionId);
        Assert.False(payment.PurposeIsProprietary);
        Assert.True(payment.CategoryPurposeIsProprietary);
        Assert.Equal("Liquidity transfer", payment.AdditionalPurpose);
    }

    [Theory]
    [InlineData("TXN-1", "TXN-1")]
    [InlineData(null, "PACS009-1")]
    public void The_caller_may_supply_the_transaction_id(string? supplied, string expected)
    {
        var payment = ValidatedPacs009.Validate(Valid() with { TransactionId = supplied }, Policy).Payment!;

        Assert.Equal(expected, payment.TransactionId);
    }

    [Theory]
    [InlineData("Debit")]
    [InlineData("Teller")]
    public void A_purpose_that_is_not_a_four_letter_code_is_proprietary(string purpose)
    {
        var payment = ValidatedPacs009.Validate(Valid() with { Purpose = purpose }, Policy).Payment!;

        Assert.True(payment.PurposeIsProprietary);
    }

    [Fact]
    public void The_paying_agent_may_be_a_configured_indirect_participant()
    {
        var result = ValidatedPacs009.Validate(Valid() with { DebtorAgent = new() { Bic = "INDRGE22", ClearingSystemMemberId = "MEMBER-1" } }, Policy);

        Assert.Equal("INDRGE22", result.Payment!.DebtorAgentBic);
    }

    public static TheoryData<string, Func<Pacs009Request, Pacs009Request>> Invalid() => new()
    {
        { "clientReference", r => r with { ClientReference = null } },
        { "clientReference", r => r with { ClientReference = new string('x', 36) } },
        { "id", r => r with { Id = null } },
        { "id", r => r with { Id = new string('x', 36) } },
        { "endToEndId", r => r with { EndToEndId = " " } },
        { "instructionId", r => r with { InstructionId = new string('x', 36) } },
        { "transactionId", r => r with { TransactionId = new string('x', 36) } },
        { "uetr", r => r with { Uetr = Guid.Empty } },
        { "instructionPriority", r => r with { InstructionPriority = 2 } },
        { "rtgsPriority", r => r with { RtgsPriority = -1 } },
        { "rejectTime", r => r with { FromTime = new TimeOnly(12, 0), RejectTime = new TimeOnly(11, 0) } },
        { "valueDate", r => r with { ValueDate = null } },
        { "currency", r => r with { Currency = "EURO" } },
        { "currency", r => r with { Currency = "USD" } },
        { "currency", r => r with { Currency = "CHF" } },
        { "amount", r => r with { Amount = 0 } },
        { "amount", r => r with { Amount = null } },
        { "debtorAgent", r => r with { DebtorAgent = null } },
        { "debtorAgent.bic", r => r with { DebtorAgent = new() { Bic = "TBCBGE22" } } },
        { "debtorAgent.bic", r => r with { DebtorAgent = new() { Bic = "bad" } } },
        { "debtorAgent.clearingSystemMemberId", r => r with { DebtorAgent = new() { Bic = "BAGAGE22", ClearingSystemMemberId = new string('x', 29) } } },
        { "creditorAgent", r => r with { CreditorAgent = null } },
        { "creditorAgent.bic", r => r with { CreditorAgent = new() { Bic = "tbc" } } },
        { "categoryPurpose.value", r => r with { CategoryPurpose = new() { Value = "TOOLONG" } } },
        { "categoryPurpose.value", r => r with { CategoryPurpose = new() { Type = 1, Value = new string('x', 36) } } },
        { "categoryPurpose.type", r => r with { CategoryPurpose = new() { Type = 2, Value = "INTC" } } },
        { "purpose", r => r with { Purpose = new string('x', 36) } },
        { "debtorAccount", r => r with { DebtorAccount = "GE29NB0000000101904918" } },
        { "creditorAccount", r => r with { CreditorAccount = "not an iban" } },
        { "debtorAccount", r => r with { DebtorAccount = "GE29 NB00 0000 0101 9049 17" } },
        { "debtorAccount", r => r with { DebtorAccount = "ge29nb0000000101904917" } },
        { "creditorAccount", r => r with { CreditorAccount = "0000000000000000000001" } }
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void An_invalid_request_is_reported_at_the_failing_field(string field, Func<Pacs009Request, Pacs009Request> break_)
    {
        var result = ValidatedPacs009.Validate(break_(Valid()), Policy);

        Assert.Null(result.Payment);
        Assert.Contains(result.Errors, error => error.Field == field);
    }
}
