using System.Globalization;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.XmlModels;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008;

internal static class Pacs008Mapping
{
    internal static MessageXml Map(ValidatedPacs008 payment, PaymentMessageContext context, Pacs008ProtocolProfile profile) => new()
    {
        Header = MapHeader(payment, context, profile),
        Document = new()
        {
            CreditTransfer = new()
            {
                Group = MapGroup(payment, context, profile),
                Transaction = MapTransaction(payment, context, profile)
            }
        }
    };

    private static HeaderXml MapHeader(ValidatedPacs008 payment, PaymentMessageContext context, Pacs008ProtocolProfile profile) => new()
    {
        From = HeaderParty(payment.ParticipantBic),
        To = HeaderParty(profile.IpsBic),
        MessageId = context.MessageId,
        MessageDefinition = Pacs008ProtocolProfile.MessageDefinition,
        CreatedAt = Timestamp(context.EnvelopeCreatedAtUtc)
    };

    private static HeaderPartyXml HeaderParty(string bic) => new()
    {
        Institution = new() { Identification = new() { Bic = bic } }
    };

    private static GroupHeaderXml MapGroup(ValidatedPacs008 payment, PaymentMessageContext context, Pacs008ProtocolProfile profile) => new()
    {
        MessageId = context.MessageId,
        CreatedAt = Timestamp(payment.CreationDateTime),
        TransactionCount = 1,
        TotalAmount = Amount(payment),
        SettlementDate = payment.AcceptanceDateTime.ToOffset(Pacs008ProtocolProfile.SettlementOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Settlement = new()
        {
            Method = SettlementMethod.Clearing,
            ClearingSystem = new() { Code = Pacs008ProtocolProfile.ClearingSystem }
        },
        PaymentType = new()
        {
            Priority = payment.Priority == PaymentPriority.High ? InstructionPriority.High : InstructionPriority.Normal,
            ServiceLevel = new() { Code = profile.ServiceLevelCode },
            LocalInstrument = new() { Code = Pacs008ProtocolProfile.LocalInstrument },
            CategoryPurpose = payment.CategoryPurposeCode is { } category ? new() { Code = category } : null
        },
        InstructingAgent = Pacs008PartyMapping.Agent(new(payment.ParticipantBic, null))
    };

    private static TransactionXml MapTransaction(ValidatedPacs008 payment, PaymentMessageContext context, Pacs008ProtocolProfile profile) => new()
    {
        Identification = new() { InstructionId = payment.InstructionId, EndToEndId = payment.EndToEndId, TransactionId = context.TransactionId },
        Amount = Amount(payment),
        AcceptedAt = Timestamp(payment.AcceptanceDateTime),
        Charges = ChargeBearer.ServiceLevel,
        UltimateDebtor = payment.UltimateDebtor is { } debtor ? Pacs008PartyMapping.Party(debtor) : null,
        Debtor = Pacs008PartyMapping.Party(payment.Debtor),
        DebtorAccount = Pacs008PartyMapping.Account(payment.DebtorAccount),
        DebtorAgent = Pacs008PartyMapping.Agent(payment.DebtorAgent),
        CreditorAgent = Pacs008PartyMapping.Agent(payment.CreditorAgent),
        Creditor = Pacs008PartyMapping.Party(payment.Creditor),
        CreditorAccount = Pacs008PartyMapping.Account(payment.CreditorAccount),
        UltimateCreditor = payment.UltimateCreditor is { } creditor ? Pacs008PartyMapping.Party(creditor) : null,
        Regulatory = Pacs008RemittanceMapping.Regulatory(payment.PaymentInitiation),
        RelatedRemittance = Pacs008RemittanceMapping.Related(payment.InitiationChannel, profile.RemittanceMethod),
        Remittance = Pacs008RemittanceMapping.Remittance(payment.Remittance)
    };

    private static AmountXml Amount(ValidatedPacs008 payment) => new()
    {
        Currency = payment.Currency,
        Value = payment.Amount.ToString("0.#####", CultureInfo.InvariantCulture)
    };

    private static string Timestamp(DateTimeOffset time) => time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
