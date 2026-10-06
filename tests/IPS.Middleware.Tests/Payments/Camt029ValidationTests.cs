using IPS.Middleware.Application.Payments.Camt029;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Recalls;
using Xunit;

namespace IPS.Middleware.Tests.Payments;

public sealed class Camt029ValidationTests
{
    private static readonly Pacs008Policy Policy = new("BAGAGE22", null, [new("GEL"), new("USD", Enabled: false)]);
    private static readonly DateOnly Today = new(2026, 10, 4);

    private static Camt029Request Valid() => new()
    {
        ClientReference = "ref-1",
        Id = "ANS-1",
        CancellationStatusId = "CST-1",
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

    [Fact]
    public void A_valid_request_is_normalized()
    {
        var result = ValidatedCamt029.Validate(Valid() with { ClientReference = " ref-1 ", AdditionalInformation = " Not a duplicate " }, Policy, Today);

        var payment = Assert.IsType<ValidatedCamt029>(result.Payment);
        Assert.Empty(result.Errors);
        Assert.Equal(("ref-1", "ANS-1", "CST-1", "BAGAGE22"), (payment.ClientReference, payment.MessageId, payment.CancellationStatusId, payment.ParticipantBic));
        Assert.Equal("Not a duplicate", payment.AdditionalInformation);
        Assert.Equal(("GEL", 100.5m), (payment.OriginalCurrency, payment.OriginalAmount));
    }

    [Fact]
    public void Blank_additional_information_is_absent()
    {
        var payment = ValidatedCamt029.Validate(Valid() with { AdditionalInformation = "   " }, Policy, Today).Payment;

        Assert.Null(payment!.AdditionalInformation);
    }

    [Fact]
    public void The_debtor_agent_may_be_another_participant_but_the_creditor_agent_must_be_us()
    {
        var other = ValidatedCamt029.Validate(Valid(), Policy, Today);
        var wrongSender = ValidatedCamt029.Validate(Valid() with
        {
            OriginalTransaction = Valid().OriginalTransaction! with { CreditorAgent = new() { Bic = "TBCBGE22" } }
        }, Policy, Today);

        Assert.NotNull(other.Payment);
        Assert.Null(wrongSender.Payment);
        Assert.Contains(wrongSender.Errors, error => error.Field == "originalTransaction.creditorAgent.bic");
    }

    public static TheoryData<string, Func<Camt029Request, Camt029Request>> Invalid() => new()
    {
        { "clientReference", r => r with { ClientReference = null } },
        { "id", r => r with { Id = null } },
        { "cancellationStatusId", r => r with { CancellationStatusId = " " } },
        { "originalMessageId", r => r with { OriginalMessageId = new string('x', 36) } },
        { "originalEndToEndId", r => r with { OriginalEndToEndId = null } },
        { "originalTransactionId", r => r with { OriginalTransactionId = null } },
        { "reasonCode", r => r with { ReasonCode = null } },
        { "reasonCode", r => r with { ReasonCode = "TOOLONG" } },
        { "reasonCode", r => r with { ReasonCode = "cust" } },
        { "additionalInformation", r => r with { AdditionalInformation = new string('x', 106) } },
        { "originalTransaction.currency", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Currency = null } } },
        { "originalTransaction.currency", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Currency = "USD" } } },
        { "originalTransaction.amount", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Amount = 0 } } },
        { "originalTransaction.amount", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Amount = null } } },
        { "originalTransaction.amount", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Amount = 1.234567m } } },
        { "originalTransaction", r => r with { OriginalTransaction = null } },
        { "originalTransaction.settlementDate", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { SettlementDate = null } } },
        { "originalTransaction.settlementDate", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { SettlementDate = new DateOnly(2026, 10, 5) } } },
        { "originalTransaction.debtor", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Debtor = null } } },
        { "originalTransaction.creditor.account", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Creditor = Valid().OriginalTransaction!.Creditor! with { Account = "GE29NB0000000101904918" } } } },
        { "originalTransaction.debtorAgent", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { DebtorAgent = null } } },
        { "originalTransaction.creditorAgent", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { CreditorAgent = null } } },
        { "originalTransaction.creditorAgent.bic", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { CreditorAgent = new() { Bic = "bad" } } } },
        { "originalTransaction.remittance.unstructured", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Remittance = new() { Unstructured = new string('x', 141) } } } }
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void An_invalid_request_is_reported_at_the_failing_field(string field, Func<Camt029Request, Camt029Request> break_)
    {
        var result = ValidatedCamt029.Validate(break_(Valid()), Policy, Today);

        Assert.Null(result.Payment);
        Assert.Contains(result.Errors, error => error.Field == field);
    }
}
