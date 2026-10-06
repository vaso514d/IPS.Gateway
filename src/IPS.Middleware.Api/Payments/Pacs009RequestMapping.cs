using IPS.Middleware.Application.Payments.Pacs009;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;

namespace IPS.Middleware.Api.Payments;

internal static class Pacs009RequestMapping
{
    public static Pacs009Request Map(Pacs009PaymentRequestDto payment)
    {
        return new()
        {
            ClientReference = payment.ClientReference,
            Id = payment.Id,
            DebtorAgent = Agent(payment.DebtorAgent),
            CreditorAgent = Agent(payment.CreditorAgent),
            InstructionId = payment.InstrId,
            EndToEndId = payment.EndToEndId,
            TransactionId = payment.TxId,
            Uetr = payment.Uetr,
            InstructionPriority = payment.InstructionPriority,
            TransactionTypeCode = payment.Ttc,
            RtgsPriority = payment.RtgsPriority,
            FromTime = payment.FromTime,
            RejectTime = payment.RejectTime,
            ValueDate = payment.ValueDate,
            Currency = payment.Currency,
            Amount = payment.Amount,
            CategoryPurpose = Code(payment.CategoryPurpose),
            Purpose = payment.Purpose,
            AdditionalPurpose = payment.AddPurpose,
            DebtorAccount = payment.Debtor?.Account,
            CreditorAccount = payment.Creditor?.Account
        };
    }

    private static Pacs009AgentInput? Agent(Pacs008AgentDto? value) => value is null ? null : new()
    {
        Bic = value.Bicfi,
        ClearingSystemMemberId = value.ClrSysMmbId
    };

    private static Pacs009CodeInput? Code(Pacs008CodeChoiceDto? value) => value is null ? null : new()
    {
        Type = value.Type,
        Value = value.Value
    };
}
