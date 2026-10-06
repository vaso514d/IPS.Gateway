using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Camt056;
using IPS.Middleware.Application.Payments.Pacs008;
using Xunit;

namespace IPS.Middleware.Tests.Payments;

public sealed class Camt056ValidationTests
{
    private static readonly Pacs008Policy Policy = new("BAGAGE22", null, [new("GEL"), new("USD", Enabled: false)]);
    private static readonly DateOnly Today = new(2026, 10, 4);

    private static Camt056Request Valid() => new()
    {
        ClientReference = "ref-1",
        Id = "RCL-1",
        RecallId = "CXL-1",
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

    [Fact]
    public void A_valid_request_is_normalized()
    {
        var result = ValidatedCamt056.Validate(Valid() with
        {
            ClientReference = " ref-1 ",
            OriginalCurrency = "GEL",
            OriginalTransaction = Valid().OriginalTransaction! with
            {
                Remittance = new() { Unstructured = " Refund ", CreditorReference = new() { Reference = " RF18 ", Issuer = " " } },
                UltimateDebtor = new() { Name = "Ultimate", Type = 1, Identifier = " 01001 " },
                Debtor = new()
                {
                    Name = " Original Payer ",
                    Account = "GE29NB0000000101904917",
                    Type = 0,
                    Identifier = "123456789",
                    Address = new() { TownName = " Tbilisi ", Country = "GE", AddressLines = [" Line 1 ", " "] }
                }
            }
        }, Policy, Today);

        var payment = Assert.IsType<ValidatedCamt056>(result.Payment);
        Assert.Empty(result.Errors);
        Assert.Equal(("ref-1", "RCL-1", "CXL-1", "BAGAGE22"), (payment.ClientReference, payment.MessageId, payment.RecallId, payment.ParticipantBic));
        Assert.Equal(new RecallRemittance("Refund", new RecallCreditorReference(null, "RF18")), payment.Original.Remittance);
        Assert.Equal(new RecallUltimateParty("Ultimate", PaymentPartyKind.Individual, "01001"), payment.Original.UltimateDebtor);
        Assert.Equal(PaymentPartyKind.Organisation, payment.Original.Debtor.Kind);
        Assert.Equal("Tbilisi", payment.Original.Debtor.Address!.TownName);
        Assert.Equal(["Line 1"], payment.Original.Debtor.Address.AddressLines);
        Assert.Null(payment.Original.Creditor.Address);
    }

    [Fact]
    public void A_settlement_date_of_today_is_allowed()
    {
        var result = ValidatedCamt056.Validate(Valid() with { OriginalSettlementDate = Today }, Policy, Today);

        Assert.NotNull(result.Payment);
    }

    public static TheoryData<string, Func<Camt056Request, Camt056Request>> Invalid() => new()
    {
        { "clientReference", r => r with { ClientReference = null } },
        { "id", r => r with { Id = null } },
        { "id", r => r with { Id = new string('x', 36) } },
        { "recallId", r => r with { RecallId = " " } },
        { "originalMessageId", r => r with { OriginalMessageId = null } },
        { "originalEndToEndId", r => r with { OriginalEndToEndId = new string('x', 36) } },
        { "originalTransactionId", r => r with { OriginalTransactionId = null } },
        { "originalCurrency", r => r with { OriginalCurrency = "USD" } },
        { "originalCurrency", r => r with { OriginalCurrency = "gel" } },
        { "originalAmount", r => r with { OriginalAmount = 0 } },
        { "originalAmount", r => r with { OriginalAmount = null } },
        { "originalAmount", r => r with { OriginalAmount = 1.234567m } },
        { "originalSettlementDate", r => r with { OriginalSettlementDate = null } },
        { "originalSettlementDate", r => r with { OriginalSettlementDate = new DateOnly(2026, 10, 5) } },
        { "reasonCode", r => r with { ReasonCode = null } },
        { "reasonCode", r => r with { ReasonCode = "TOOLONG" } },
        { "reasonCode", r => r with { ReasonCode = "dupl" } },
        { "originalTransaction", r => r with { OriginalTransaction = null } },
        { "originalTransaction.settlementDate", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { SettlementDate = null } } },
        { "originalTransaction.settlementDate", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { SettlementDate = new DateOnly(2026, 10, 5) } } },
        { "originalTransaction.debtor", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Debtor = null } } },
        { "originalTransaction.debtor.name", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Debtor = Valid().OriginalTransaction!.Debtor! with { Name = " " } } } },
        { "originalTransaction.debtor.account", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Debtor = Valid().OriginalTransaction!.Debtor! with { Account = "GE29NB0000000101904918" } } } },
        { "originalTransaction.debtor.type", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Debtor = Valid().OriginalTransaction!.Debtor! with { Identifier = "01001" } } } },
        { "originalTransaction.debtor.address.country", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Debtor = Valid().OriginalTransaction!.Debtor! with { Address = new() { Country = "GEO" } } } } },
        { "originalTransaction.debtor.address.addressLines", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Debtor = Valid().OriginalTransaction!.Debtor! with { Address = new() { AddressLines = [.. Enumerable.Repeat("x", 8)] } } } } },
        { "originalTransaction.creditor", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Creditor = null } } },
        { "originalTransaction.creditor.account", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Creditor = Valid().OriginalTransaction!.Creditor! with { Account = "not an iban" } } } },
        { "originalTransaction.ultimateCreditor.name", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { UltimateCreditor = new() { Name = null } } } },
        { "originalTransaction.debtorAgent", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { DebtorAgent = null } } },
        { "originalTransaction.debtorAgent.bic", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { DebtorAgent = new() { Bic = "TBCBGE22" } } } },
        { "originalTransaction.debtorAgent.bic", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { DebtorAgent = new() { Bic = "bad" } } } },
        { "originalTransaction.creditorAgent", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { CreditorAgent = null } } },
        { "originalTransaction.remittance.unstructured", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Remittance = new() { Unstructured = new string('x', 141) } } } },
        { "originalTransaction.remittance.creditorReference.reference", r => r with { OriginalTransaction = Valid().OriginalTransaction! with { Remittance = new() { CreditorReference = new() { Issuer = "Bank" } } } } }
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void An_invalid_request_is_reported_at_the_failing_field(string field, Func<Camt056Request, Camt056Request> break_)
    {
        var result = ValidatedCamt056.Validate(break_(Valid()), Policy, Today);

        Assert.Null(result.Payment);
        Assert.Contains(result.Errors, error => error.Field == field);
    }
}
