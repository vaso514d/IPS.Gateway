using IPS.Middleware.Application.Inbound.Transfers;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;

namespace IPS.Middleware.Infrastructure.Inbound.Transfers;

// The core-facing JSON of a received pacs.009. No client reference is set: the core keys on the EndToEndId.
internal static class IncomingPacs009CoreMapping
{
    internal static Pacs009PaymentRequestDto ToContract(IncomingPacs009 transfer) => new()
    {
        Id = transfer.MessageId,
        DebtorAgent = Agent(transfer.DebtorAgent),
        CreditorAgent = Agent(transfer.CreditorAgent),
        InstrId = transfer.InstructionId,
        EndToEndId = transfer.EndToEndId,
        TxId = transfer.TransactionId,
        Uetr = transfer.Uetr,
        InstructionPriority = transfer.InstructionPriority,
        ValueDate = transfer.ValueDate,
        Currency = transfer.Currency,
        Amount = transfer.Amount,
        CategoryPurpose = transfer.CategoryPurpose is { } code ? new Pacs008CodeChoiceDto { Type = code.Type, Value = code.Value } : null,
        Purpose = transfer.Purpose,
        AddPurpose = transfer.AdditionalPurpose,
        Debtor = Account(transfer.DebtorAccount),
        Creditor = Account(transfer.CreditorAccount)
    };

    private static Pacs008AgentDto Agent(IncomingAgent agent) => new() { Bicfi = agent.Bic, ClrSysMmbId = agent.ClearingSystemMemberId };

    private static Pacs009AccountDto? Account(string? account) => account is null ? null : new Pacs009AccountDto { Account = account };
}
