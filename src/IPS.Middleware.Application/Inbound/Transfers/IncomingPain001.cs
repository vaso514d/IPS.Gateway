using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Inbound.Transfers;

// The verified content of a payment initiation (pain.001) a PISP submitted through IPS for an account of ours. The core
// system answers IPS itself: it accepts with a pacs.008 whose EndToEndId is "PSP-" plus the initiation id, or refuses.
// Debtor, creditor, ultimate parties and remittance use the same shapes as a pacs.008.
public sealed record IncomingPain001(
    string MessageId,
    DateTimeOffset CreatedAt,
    IncomingInitiatingParty? InitiatingParty,
    string PaymentInformationId,
    string? ServiceLevelCode,
    string? LocalInstrumentCode,
    string? CategoryPurposeCode,
    DateOnly? RequestedExecutionDate,
    Pacs008DebtorInput Debtor,
    Pacs008UltimatePartyInput? UltimateDebtor,
    string? InstructionId,
    string? EndToEndId,
    decimal Amount,
    string Currency,
    Pacs008CreditorInput? Creditor,
    Pacs008UltimatePartyInput? UltimateCreditor,
    string? PurposeCode,
    Pacs008RemittanceInput? Remittance) : IIncomingTransferContent
{
    // "PSP-" plus the initiation id must fit the 35-character EndToEndId of the accepting pacs.008.
    public const int MaxPaymentInformationIdLength = 31;

    string IIncomingTransferContent.Kind => PaymentMessageTypes.Pain001;

    string IIncomingTransferContent.Key => PaymentInformationId;

    // The debtor's agent is the bank that holds the account, which must be us.
    string IIncomingTransferContent.ReceiverBic => Debtor.ParticipantBic!;

    CoreIdentifiers IIncomingTransferContent.Echo => new(null, PaymentInformationId, null);

    // Lists compare by reference, so the structured remittance is copied into a list that compares by contents.
    public IncomingPain001 Frozen() => Remittance is { Structured: { } structured }
        ? this with { Remittance = Remittance with { Structured = ValueList<Pacs008StructuredRemittanceInput>.Copy(structured) } }
        : this;
}

public sealed record IncomingInitiatingParty(string? Name, string? Bic);
