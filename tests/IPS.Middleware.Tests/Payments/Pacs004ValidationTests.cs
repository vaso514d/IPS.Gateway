using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs004;
using IPS.Middleware.Application.Payments.Pacs008;
using Xunit;

namespace IPS.Middleware.Tests.Payments;

public sealed class Pacs004ValidationTests
{
    private static readonly Pacs008Policy Policy = new("BAGAGE22", null, [new("GEL"), new("USD", Enabled: false)], ["INDRGE22"]);

    private static Pacs004Request Valid() => new()
    {
        ClientReference = "ref-1",
        Id = "RTR-1",
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

    [Fact]
    public void A_valid_request_is_normalized_with_a_full_return_by_default()
    {
        var result = ValidatedPacs004.Validate(Valid() with
        {
            ClientReference = " ref-1 ",
            ReturnReasonCode = " ",
            InstructingAgent = "BAGAGE22",
            SenderIndirectParticipant = "INDRGE22",
            Debtor = new() { Name = " Original Payer ", Account = "GE95TB0000000123456789", Type = 1, Identifier = " 01001 " },
            Original = Valid().Original! with { OriginalMessageId = "M-1", OriginalMessageNameId = " pacs.008.001.12 " }
        }, Policy);

        var payment = Assert.IsType<ValidatedPacs004>(result.Payment);
        Assert.Empty(result.Errors);
        Assert.Equal("ref-1", payment.ClientReference);
        Assert.Equal("RTR-1", payment.ReturnId);
        Assert.Equal("BAGAGE22", payment.ParticipantBic);
        Assert.Equal("FOCR", payment.ReturnReasonCode);
        Assert.Equal("INDRGE22", payment.SenderIndirectParticipant);
        Assert.Equal(new ReturnedParty("Original Payer", PaymentPartyKind.Individual, "01001", "GE95TB0000000123456789"), payment.Debtor);
        Assert.Null(payment.Creditor.Kind);
        Assert.Equal(("M-1", "pacs.008.001.12"), (payment.Original.MessageId, payment.Original.MessageNameId));
    }

    [Fact]
    public void The_original_amount_defaults_to_the_returned_amount_and_currency()
    {
        var payment = ValidatedPacs004.Validate(Valid(), Policy).Payment!;

        Assert.Equal(100.5m, payment.Original.Amount);
        Assert.Equal("GEL", payment.Original.Currency);
    }

    [Fact]
    public void A_larger_original_amount_is_kept_and_a_different_currency_is_not_compared()
    {
        var larger = ValidatedPacs004.Validate(Valid() with { Original = Valid().Original! with { Amount = 250m } }, Policy).Payment!;
        var other = ValidatedPacs004.Validate(Valid() with { Original = Valid().Original! with { Amount = 1m, Currency = "USD" } }, Policy).Payment!;

        Assert.Equal(250m, larger.Original.Amount);
        Assert.Equal(("USD", 1m), (other.Original.Currency, other.Original.Amount));
    }

    public static TheoryData<string, Func<Pacs004Request, Pacs004Request>> Invalid() => new()
    {
        { "clientReference", r => r with { ClientReference = null } },
        { "id", r => r with { Id = new string('x', 36) } },
        { "id", r => r with { Id = null } },
        { "amount", r => r with { Amount = 0 } },
        { "amount", r => r with { Amount = null } },
        { "amount", r => r with { Amount = 1.234567m } },
        { "amount", r => r with { Amount = 10000000000000m } },
        { "original.amount", r => r with { Original = Valid().Original! with { Amount = 100.123456m } } },
        { "currency", r => r with { Currency = "USD" } },
        { "currency", r => r with { Currency = "gel1" } },
        { "valueDate", r => r with { ValueDate = null } },
        { "transactionTypeCode", r => r with { TransactionTypeCode = new string('x', 36) } },
        { "instructedAgent", r => r with { InstructedAgent = null } },
        { "instructedAgent", r => r with { InstructedAgent = "bad" } },
        { "instructingAgent", r => r with { InstructingAgent = "TBCBGE22" } },
        { "senderIndirectParticipant", r => r with { SenderIndirectParticipant = "UNKNOWN" } },
        { "returnReasonCode", r => r with { ReturnReasonCode = "AC03" } },
        { "returnReasonCode", r => r with { ReturnReasonCode = "focr" } },
        { "debtorSwift", r => r with { DebtorSwift = "x" } },
        { "creditorSwift", r => r with { CreditorSwift = "x" } },
        { "debtor", r => r with { Debtor = null } },
        { "debtor.name", r => r with { Debtor = Valid().Debtor! with { Name = " " } } },
        { "debtor.account", r => r with { Debtor = Valid().Debtor! with { Account = "GE95TB0000000123456780" } } },
        { "debtor.account", r => r with { Debtor = Valid().Debtor! with { Account = null } } },
        { "debtor.type", r => r with { Debtor = Valid().Debtor! with { Identifier = "01001" } } },
        { "debtor.type", r => r with { Debtor = Valid().Debtor! with { Type = 2 } } },
        { "creditor", r => r with { Creditor = null } },
        { "creditor.name", r => r with { Creditor = Valid().Creditor! with { Name = new string('x', 141) } } },
        { "creditor.account", r => r with { Creditor = Valid().Creditor! with { Account = "not an iban" } } },
        { "originatorName", r => r with { OriginatorName = new string('x', 141) } },
        { "additionalInfo", r => r with { AdditionalInfo = new string('x', 106) } },
        { "original", r => r with { Original = null } },
        { "original.transactionId", r => r with { Original = Valid().Original! with { TransactionId = null } } },
        { "original.endToEndId", r => r with { Original = Valid().Original! with { EndToEndId = " " } } },
        { "original.valueDate", r => r with { Original = Valid().Original! with { ValueDate = null } } },
        { "original.amount", r => r with { Original = Valid().Original! with { Amount = 0 } } },
        { "original.currency", r => r with { Original = Valid().Original! with { Currency = "EURO" } } },
        { "original.amount", r => r with { Original = Valid().Original! with { Currency = "USD" } } },
        { "original.uetr", r => r with { Original = Valid().Original! with { Uetr = Guid.Empty } } },
        { "original.originalMessageId", r => r with { Original = Valid().Original! with { OriginalMessageId = new string('x', 36) } } },
        { "amount", r => r with { Amount = 200m, Original = Valid().Original! with { Amount = 100m } } }
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void An_invalid_request_is_reported_at_the_failing_field(string field, Func<Pacs004Request, Pacs004Request> break_)
    {
        var result = ValidatedPacs004.Validate(break_(Valid()), Policy);

        Assert.Null(result.Payment);
        Assert.Contains(result.Errors, error => error.Field == field);
    }
}
