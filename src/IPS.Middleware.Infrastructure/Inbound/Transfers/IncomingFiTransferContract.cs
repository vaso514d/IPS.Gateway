using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Domain.Inbound;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;

namespace IPS.Middleware.Infrastructure.Inbound.Transfers;

// The asynchronous delivery of an incoming pacs.009 to the core system (specification 023): the gateway's transactionId,
// the operation type and the transfer with the IPS references and the registration time.
internal static class IncomingFiTransferContract
{
    // The transfer's identity in the gateway, fixed for its life: the idempotency key and the status reference.
    internal static string TransactionId(IncomingTransfer transfer) => transfer.Id.ToString("D");

    internal static IncomingFiTransferRequestDto Map(IncomingTransfer transfer, IncomingPacs009 content) => new()
    {
        TransactionId = TransactionId(transfer),
        OperationType = IncomingOperationTypes.FiTransfer,
        Data = new()
        {
            MessageId = content.MessageId,
            InstructionId = content.InstructionId,
            EndToEndId = content.EndToEndId,
            IsoTransactionId = content.TransactionId,
            ReceivedAt = transfer.RegisteredAtUtc,
            Amount = content.Amount,
            Currency = content.Currency,
            SettlementDate = content.ValueDate,
            Debtor = new()
            {
                ParticipantBic = content.DebtorAgent.Bic,
                DirectParticipantBic = content.InstructingAgentBic,
                AccountIban = content.DebtorAccount
            },
            Creditor = new() { ParticipantBic = content.CreditorAgent.Bic, AccountIban = content.CreditorAccount },
            CategoryPurposeCode = content.CategoryPurpose?.Value,
            PurposeCode = content.Purpose,
            RemittanceInformation = content.AdditionalPurpose
        }
    };
}
