using IPS.Middleware.Application.Payments;

namespace IPS.Middleware.Application.Inbound.Transfers;

// The verified content of a payment return received from IPS. The return reason is kept as received; a return is not
// matched to the outgoing payment it names.
public sealed record IncomingPacs004(
    string ReturnId,
    decimal Amount,
    string Currency,
    DateOnly? ValueDate,
    string? ReturnReasonCode,
    string? AdditionalInformation,
    string InstructedAgent,
    string InstructingAgent,
    string? SenderMemberId,
    IncomingReturnParty? Debtor,
    IncomingReturnParty? Creditor,
    IncomingOriginalPayment Original) : IIncomingTransferContent
{
    string IIncomingTransferContent.Kind => PaymentMessageTypes.Pacs004;

    string IIncomingTransferContent.Key => ReturnId;

    // The instructed agent is the bank of the original debtor, who gets the money back.
    string IIncomingTransferContent.ReceiverBic => InstructedAgent;

    // The core's echo of the original ids is not known, so only the return id is checked.
    CoreIdentifiers IIncomingTransferContent.Echo => new(null, ReturnId, null);
}

// Type 0 is an organisation, type 1 an individual; the identifier is present only with its type.
public sealed record IncomingReturnParty(string? Name, int? Type, string? Identifier, string? Account);

public sealed record IncomingOriginalPayment(
    string TransactionId,
    string? InstructionId,
    string? EndToEndId,
    Guid? Uetr,
    DateOnly? ValueDate,
    string? MessageId,
    string? MessageNameId,
    DateTimeOffset? CreationDateTime,
    decimal? Amount,
    string? Currency);
