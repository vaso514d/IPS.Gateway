using IPS.Middleware.Application.Payments.Pacs004;
using IPS.MiidleWear.Contracts.Pacs004;
using IPS.MiidleWear.Contracts.Pacs008;

namespace IPS.Middleware.Api.Payments;

internal static class Pacs004RequestMapping
{
    public static Pacs004Request Map(Pacs004PaymentReturnRequestDto payment) => new()
    {
        ClientReference = payment.ClientReference,
        Id = payment.Id,
        Amount = payment.Amount,
        Currency = payment.Currency,
        ValueDate = payment.ValueDate,
        TransactionTypeCode = payment.Ttc,
        InstructingAgent = payment.InstructingAgent,
        SenderIndirectParticipant = payment.SenderClrSysMmbId,
        InstructedAgent = payment.InstructedAgent,
        ReturnReasonCode = payment.ReturnReasonCode,
        DebtorSwift = payment.DebtorSwift,
        CreditorSwift = payment.CreditorSwift,
        Debtor = Party(payment.Debtor),
        Creditor = Party(payment.Creditor),
        OriginatorName = payment.OriginatorName,
        OriginatorAddress = payment.OriginatorAddress,
        AdditionalInfo = payment.AdditionalInfo,
        Original = Original(payment.Original)
    };

    private static Pacs004PartyInput? Party(Pacs008PartyDto? value) => value is null ? null : new()
    {
        Type = value.Type,
        Name = value.Name,
        Identifier = value.Id,
        Account = value.Account
    };

    private static Pacs004OriginalInput? Original(Pacs004OriginalPaymentReferenceDto? value) => value is null ? null : new()
    {
        TransactionId = value.TransactionId,
        InstructionId = value.InstructionId,
        EndToEndId = value.EndToEndId,
        Uetr = value.Uetr,
        ValueDate = value.ValueDate,
        OriginalMessageId = value.OriginalMessageId,
        OriginalMessageNameId = value.OriginalMessageNameId,
        Amount = value.Amount,
        Currency = value.Currency
    };
}
