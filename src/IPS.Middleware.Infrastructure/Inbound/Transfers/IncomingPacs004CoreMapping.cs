using IPS.Middleware.Application.Inbound.Transfers;
using IPS.MiidleWear.Contracts.Pacs004;
using IPS.MiidleWear.Contracts.Pacs008;

namespace IPS.Middleware.Infrastructure.Inbound.Transfers;

// The core-facing JSON of a received pacs.004. No client reference is set: the core keys on the return id.
internal static class IncomingPacs004CoreMapping
{
    internal static Pacs004PaymentReturnRequestDto ToContract(IncomingPacs004 payment) => new()
    {
        Id = payment.ReturnId,
        Amount = payment.Amount,
        Currency = payment.Currency,
        ValueDate = payment.ValueDate,
        InstructingAgent = payment.InstructingAgent,
        SenderClrSysMmbId = payment.SenderMemberId,
        InstructedAgent = payment.InstructedAgent,
        ReturnReasonCode = payment.ReturnReasonCode,
        Debtor = Party(payment.Debtor),
        Creditor = Party(payment.Creditor),
        AdditionalInfo = payment.AdditionalInformation,
        Original = new Pacs004OriginalPaymentReferenceDto
        {
            TransactionId = payment.Original.TransactionId,
            InstructionId = payment.Original.InstructionId,
            EndToEndId = payment.Original.EndToEndId,
            Uetr = payment.Original.Uetr,
            ValueDate = payment.Original.ValueDate,
            OriginalMessageId = payment.Original.MessageId,
            OriginalMessageNameId = payment.Original.MessageNameId,
            OriginalCreationDateTime = payment.Original.CreationDateTime,
            Amount = payment.Original.Amount,
            Currency = payment.Original.Currency
        }
    };

    private static Pacs008PartyDto? Party(IncomingReturnParty? party) => party is null ? null : new()
    {
        Name = party.Name,
        Account = party.Account,
        Type = party.Type,
        Id = party.Identifier
    };
}
