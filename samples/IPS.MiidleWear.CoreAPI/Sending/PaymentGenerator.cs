using IPS.MiidleWear.Contracts.Camt056;
using IPS.MiidleWear.Contracts.Pacs004;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;
using Microsoft.Extensions.Options;

namespace IPS.MiidleWear.CoreAPI.Sending;

// Random but valid sends, as a core system would make them: they pass the gateway's validation, so the gateway signs and
// submits them to IPS. Follows the original gateway's generator (IPS.MiidleWear.JsonGenerator.API) with the rebuilt
// gateway's rules: an acceptance time within one second of creation, and FOCR as the only return reason.
public sealed class PaymentGenerator(IOptions<GatewayOptions> options)
{
    private const string FullReturnReason = "FOCR";
    private const string DuplicateRecallReason = "DUPL";

    private static readonly string[] Purposes =
    [
        "საქონლის ღირებულების ანაზღაურება",
        "მომსახურების საფასურის გადახდა",
        "ხელფასის გადარიცხვა",
        "იჯარის გადასახადი"
    ];

    private static readonly string[] Names =
    [
        "გიორგი ბერიძე", "ნინო კაპანაძე", "დავით მაისურაძე", "მარიამ ლომიძე", "ლევან გელაშვილი", "ანა ჯავახიშვილი"
    ];

    private static readonly string[] CategoryPurposes = ["CASH", "GOVT", "SALA", "TAXS"];
    private static readonly string[] Channels = ["IB", "MB", "BR"];

    private GatewayOptions Settings => options.Value;

    // The accounts are random IBANs at the debtor's and creditor's banks unless given.
    public Pacs008InstantPaymentRequestDto Pacs008(
        string? currency,
        string? creditorBic,
        decimal? amount,
        string? debtorAccount = null,
        string? creditorAccount = null)
    {
        var creditorBank = CreditorBank(creditorBic);
        var createdAt = DateTimeOffset.Now;
        var stamp = TestIdentifiers.Digits(10);
        return new Pacs008InstantPaymentRequestDto
        {
            ClientReference = $"CBS-{createdAt:yyyyMMdd}-{stamp}",
            InstructionId = $"INST-{stamp}",
            EndToEndId = $"E2E-{TestIdentifiers.Digits(10)}",
            CreationDateTime = createdAt,
            AcceptanceDateTime = createdAt.AddMilliseconds(Random.Shared.Next(0, 900)),
            Amount = Amount(amount),
            Currency = Currency(currency),
            InstructionPriority = Pick(["NORM", "HIGH"]),
            CategoryPurposeCode = Pick(CategoryPurposes),
            Debtor = Debtor(debtorAccount),
            Creditor = Creditor(creditorBank, creditorAccount),
            PaymentInitiation = new Pacs008PaymentInitiationDto { ChannelCode = Pick(Channels) },
            Remittance = new Pacs008RemittanceDto { Unstructured = Pick(Purposes) }
        };
    }

    public Pacs009PaymentRequestDto Pacs009(DateOnly? valueDate, string? currency, string? creditorBic, decimal? amount)
    {
        var creditorBank = CreditorBank(creditorBic);
        var date = valueDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return new Pacs009PaymentRequestDto
        {
            ClientReference = $"CBS9-{date:yyyyMMdd}-{TestIdentifiers.Digits(10)}",
            Id = $"PMT9-{date:yyyyMMdd}-{TestIdentifiers.Digits(4)}",
            DebtorAgent = new Pacs008AgentDto { Bicfi = Settings.ParticipantBic },
            CreditorAgent = new Pacs008AgentDto { Bicfi = creditorBank },
            InstrId = $"INST-{TestIdentifiers.Digits(10)}",
            EndToEndId = $"E2E-{TestIdentifiers.Digits(10)}",
            TxId = $"TX-{TestIdentifiers.Digits(10)}",
            Uetr = Guid.NewGuid(),
            InstructionPriority = Random.Shared.Next(0, 2),
            RtgsPriority = Random.Shared.Next(0, 2),
            ValueDate = date,
            Currency = Currency(currency),
            Amount = Amount(amount),
            Purpose = Pick(Purposes),
            Debtor = new Pacs009AccountDto { Account = Iban(Settings.ParticipantBic) },
            Creditor = new Pacs009AccountDto { Account = Iban(creditorBank) }
        };
    }

    // Returns a payment the bank received: the debtor is the original payer at the bank the money goes back to, the creditor
    // the original payee at this bank, who returns it.
    public Pacs004PaymentReturnRequestDto Pacs004(DateOnly? valueDate, string? currency, string? creditorBic, decimal? amount)
    {
        var returnedToBank = CreditorBank(creditorBic);
        var date = valueDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var returned = Amount(amount);
        var resolvedCurrency = Currency(currency);
        return new Pacs004PaymentReturnRequestDto
        {
            ClientReference = $"CBS4-{date:yyyyMMdd}-{TestIdentifiers.Digits(10)}",
            Id = $"RET-{date:yyyyMMdd}-{TestIdentifiers.Digits(4)}",
            Amount = returned,
            Currency = resolvedCurrency,
            ValueDate = date,
            InstructedAgent = returnedToBank,
            ReturnReasonCode = FullReturnReason,
            DebtorSwift = returnedToBank,
            Debtor = Party(returnedToBank),
            CreditorSwift = Settings.ParticipantBic,
            Creditor = Party(Settings.ParticipantBic),
            OriginatorName = Pick(Names),
            AdditionalInfo = Pick(Purposes),
            Original = new Pacs004OriginalPaymentReferenceDto
            {
                TransactionId = $"TX-{TestIdentifiers.Digits(10)}",
                InstructionId = $"INST-{TestIdentifiers.Digits(10)}",
                EndToEndId = $"E2E-{TestIdentifiers.Digits(10)}",
                Uetr = Guid.NewGuid(),
                ValueDate = date,
                OriginalMessageId = $"MSG-{TestIdentifiers.Digits(10)}",
                OriginalMessageNameId = "pacs.008.001.12",
                OriginalCreationDateTime = DateTimeOffset.UtcNow.AddMinutes(-5),
                Amount = returned,
                Currency = resolvedCurrency
            }
        };
    }

    // Recalls a pacs.008 this core sent: the original references, amount, settlement date and parties come from the sent
    // request and the gateway's answer. The transaction id is generated by the gateway and is not in its answer, so the
    // caller supplies it.
    public Camt056RecallRequestDto Camt056(SentPacs008 sent, string originalTransactionId, string? reasonCode)
    {
        var original = sent.Request;
        var createdAt = DateTimeOffset.Now;
        return new Camt056RecallRequestDto
        {
            ClientReference = $"CBS56-{createdAt:yyyyMMdd}-{TestIdentifiers.Digits(10)}",
            Id = $"RCLMSG-{createdAt:yyyyMMdd}-{TestIdentifiers.Digits(6)}",
            CreDtTm = createdAt,
            RecallId = $"RCL-{TestIdentifiers.Digits(10)}",
            OriginalMessageId = sent.MessageId,
            OriginalEndToEndId = original.EndToEndId,
            OriginalTransactionId = originalTransactionId.Trim(),
            OriginalCurrency = original.Currency,
            OriginalAmount = original.Amount,
            OriginalSettlementDate = SettlementDate(original),
            ReasonCode = RecallReason(reasonCode),
            OriginalTransaction = new Camt056OriginalTransactionReferenceDto
            {
                SettlementDate = SettlementDate(original),
                RemittanceInformation = original.Remittance?.Unstructured is { } text ? new Camt056RemittanceInformationDto { Unstructured = text } : null,
                Debtor = Party(original.Debtor?.Name, original.Debtor?.Type, original.Debtor?.Identifier, original.Debtor?.Account),
                DebtorAgent = new Camt056AgentDto { Bicfi = Settings.ParticipantBic },
                CreditorAgent = new Camt056AgentDto { Bicfi = original.Creditor?.ParticipantBic },
                Creditor = Party(original.Creditor?.Name, original.Creditor?.Type, original.Creditor?.Identifier, original.Creditor?.Account)
            }
        };
    }

    // The gateway settles a pacs.008 on its acceptance day in Georgian time (Pacs008Message.SettlementOffset).
    private static DateOnly? SettlementDate(Pacs008InstantPaymentRequestDto payment) =>
        payment.AcceptanceDateTime is { } accepted ? DateOnly.FromDateTime(accepted.ToOffset(TimeSpan.FromHours(4)).DateTime) : null;

    private static Camt056PartyDto Party(string? name, int? type, string? identifier, string? account) =>
        new() { Name = name, Type = type, Id = identifier, Account = account };

    private Pacs008DebtorRequestDto Debtor(string? account)
    {
        var type = Random.Shared.Next(0, 2);
        return new Pacs008DebtorRequestDto
        {
            Type = type,
            Name = Pick(Names),
            Identifier = Identifier(type),
            Account = Account(account, Settings.ParticipantBic)
        };
    }

    private Pacs008CreditorRequestDto Creditor(string bank, string? account)
    {
        var type = Random.Shared.Next(0, 2);
        return new Pacs008CreditorRequestDto
        {
            Type = type,
            Name = Pick(Names),
            Identifier = Identifier(type),
            ParticipantBic = bank,
            Account = Account(account, bank)
        };
    }

    private Pacs008PartyDto Party(string bank)
    {
        var type = Random.Shared.Next(0, 2);
        return new Pacs008PartyDto
        {
            Name = Pick(Names),
            Account = Iban(bank),
            Type = type,
            Id = Identifier(type)
        };
    }

    // A random IBAN of the bank, with its configured IBAN bank code.
    private string Iban(string bic) => TestIdentifiers.Iban(Settings.IbanCode(bic));

    // The account given in the query, without spaces, or a random one at the bank.
    private string Account(string? account, string bic) =>
        string.IsNullOrWhiteSpace(account) ? Iban(bic) : string.Concat(account.Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();

    // 0 is an organisation (a tax code with a valid check digit), 1 an individual (an eleven-digit personal number).
    private static string Identifier(int type) => type == 0 ? TestIdentifiers.OrganisationTaxCode() : TestIdentifiers.PersonalNumber();

    private string CreditorBank(string? bic) =>
        string.IsNullOrWhiteSpace(bic) ? Pick(Settings.CreditorBanks.Keys.ToArray()) : bic.Trim().ToUpperInvariant();

    private string Currency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? Pick(Settings.Currencies) : currency.Trim().ToUpperInvariant();

    private decimal Amount(decimal? amount) => amount ?? Pick(Settings.Amounts);

    private string RecallReason(string? reasonCode) =>
        !string.IsNullOrWhiteSpace(reasonCode) ? reasonCode.Trim().ToUpperInvariant()
        : Settings.RecallReasons.Length > 0 ? Pick(Settings.RecallReasons)
        : DuplicateRecallReason;

    private static T Pick<T>(IReadOnlyList<T> values) => values[Random.Shared.Next(values.Count)];
}
