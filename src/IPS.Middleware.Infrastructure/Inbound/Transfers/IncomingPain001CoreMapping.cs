using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.MiidleWear.Contracts.Pain001;

namespace IPS.Middleware.Infrastructure.Inbound.Transfers;

// The core-facing JSON of a received pain.001. No client reference is set: the core keys on the initiation id.
internal static class IncomingPain001CoreMapping
{
    internal static Pain001PaymentInitiationDto ToContract(IncomingPain001 initiation) => new()
    {
        MessageId = initiation.MessageId,
        CreationDateTime = initiation.CreatedAt,
        InitiatingParty = initiation.InitiatingParty is { } party ? new Pain001InitiatingPartyDto { Name = party.Name, Bic = party.Bic } : null,
        PaymentInformationId = initiation.PaymentInformationId,
        ServiceLevelCode = initiation.ServiceLevelCode,
        LocalInstrumentCode = initiation.LocalInstrumentCode,
        CategoryPurposeCode = initiation.CategoryPurposeCode,
        RequestedExecutionDate = initiation.RequestedExecutionDate,
        Debtor = IncomingPacs008CoreMapping.Debtor(initiation.Debtor),
        UltimateDebtor = IncomingPacs008CoreMapping.Ultimate(initiation.UltimateDebtor),
        InstructionId = initiation.InstructionId,
        EndToEndId = initiation.EndToEndId,
        Amount = initiation.Amount,
        Currency = initiation.Currency,
        Creditor = IncomingPacs008CoreMapping.Creditor(initiation.Creditor),
        UltimateCreditor = IncomingPacs008CoreMapping.Ultimate(initiation.UltimateCreditor),
        PurposeCode = initiation.PurposeCode,
        Remittance = IncomingPacs008CoreMapping.Remittance(initiation.Remittance)
    };
}
