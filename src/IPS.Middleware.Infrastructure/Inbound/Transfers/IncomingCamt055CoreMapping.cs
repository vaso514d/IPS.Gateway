using System.Text.Json;
using System.Text.Json.Serialization;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.MiidleWear.Contracts.Camt055;

namespace IPS.Middleware.Infrastructure.Inbound.Transfers;

// The core-facing JSON of a received camt.055. No client reference exists on the contract: the core keys on the message id.
internal static class IncomingCamt055CoreMapping
{
    private static readonly JsonSerializerOptions AddressJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static Camt055CancellationRequestDto ToContract(IncomingCamt055 cancellation) => new()
    {
        MsgId = cancellation.MessageId,
        AssignerBic = cancellation.AssignerBic,
        AssigneeBic = cancellation.AssigneeBic,
        // The contract carries plain date-times, so instants are given in UTC.
        CreDtTm = cancellation.CreatedAt.UtcDateTime,
        PaymentCancellationId = cancellation.PaymentCancellationId,
        OriginalPaymentInformationId = cancellation.OriginalPaymentInformationId,
        OriginalGroupInformation = cancellation.OriginalGroup is { } group
            ? new Camt055OriginalGroupInformationDto
            {
                OriginalMessageId = group.MessageId,
                OriginalMessageNameId = group.MessageNameId,
                OriginalCreationDateTime = group.CreatedAt?.UtcDateTime
            }
            : null,
        CancellationId = cancellation.CancellationId,
        OriginalInstructionId = cancellation.OriginalInstructionId,
        OriginalEndToEndId = cancellation.OriginalEndToEndId,
        CancellationReason = cancellation.Reason is { } reason
            ? new Camt055CancellationReasonDto
            {
                OriginatorName = reason.OriginatorName,
                OriginatorBic = reason.OriginatorBic,
                ReasonCode = reason.ReasonCode,
                AdditionalInformation = reason.AdditionalInformation
            }
            : null,
        OriginalTransaction = Original(cancellation.Original)
    };

    private static Camt055OriginalTransactionReferenceDto Original(IncomingCancelledInitiation original) => new()
    {
        Currency = original.Currency,
        Amount = original.Amount,
        RequestedExecutionDate = original.RequestedExecutionDate?.ToDateTime(TimeOnly.MinValue),
        ServiceLevelCode = original.ServiceLevelCode,
        LocalInstrumentCode = original.LocalInstrumentCode,
        CategoryPurposeCode = original.CategoryPurposeCode,
        RemittanceInformationUnstructured = original.RemittanceUnstructured,
        DebtorAgent = new Camt055AgentDto { Bicfi = original.DebtorAgent.Bic, Name = original.DebtorAgent.Name },
        CreditorAgent = original.CreditorAgent is { } agent ? new Camt055AgentDto { Bicfi = agent.Bic, Name = agent.Name } : null,
        Creditor = original.Creditor is { } creditor
            ? new Camt055PartyDto
            {
                Name = creditor.Name,
                Id = creditor.Identifier,
                // The contract leaves the address shape open; it is given in the shape of the recall contracts.
                PostalAddress = IncomingRecallCoreMapping.Address(creditor.Address) is { } address
                    ? JsonSerializer.SerializeToElement(address, AddressJson)
                    : null
            }
            : null,
        CreditorAccount = original.CreditorAccount is { } iban ? new Camt055AccountDto { Iban = iban } : null
    };
}
