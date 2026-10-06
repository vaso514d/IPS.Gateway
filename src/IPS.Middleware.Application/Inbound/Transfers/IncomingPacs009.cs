using IPS.Middleware.Application.Payments;

namespace IPS.Middleware.Application.Inbound.Transfers;

// The verified content of a transfer received from IPS, as the core system is told about it. The members of the
// interface are implemented explicitly, so they never enter the stored JSON.
public interface IIncomingTransferContent
{
    // The message type of the transfer; with the participant it separates keys of different kinds.
    string Kind { get; }

    // The business key, also the core idempotency key and status reference.
    string Key { get; }

    // The participant the transfer is addressed to; it must be ours.
    string ReceiverBic { get; }

    // The identifiers a core reply may echo; one the transfer does not carry is not checked.
    CoreIdentifiers Echo { get; }
}

public sealed record CoreIdentifiers(string? EndToEndId, string? Id, string? TransactionId);

public abstract record IncomingTransferReadResult
{
    public sealed record Ready(IIncomingTransferContent Transfer) : IncomingTransferReadResult;

    public sealed record Hold(string Reason) : IncomingTransferReadResult;
}

public interface IIncomingTransferProtocol
{
    // Whether this protocol reads receipts of the message type (short type or full definition).
    bool Reads(string messageType);

    // Ready only for a trusted, schema-valid single transfer with everything the core needs; otherwise Hold. A value date
    // IPS did not send stays absent, so a redelivery on another day is still the same content.
    IncomingTransferReadResult Read(string xml);
}

// Records compare by value, so a redelivery is recognized by equality.
public sealed record IncomingPacs009(
    string MessageId,
    string EndToEndId,
    string? InstructionId,
    string? TransactionId,
    Guid? Uetr,
    int? InstructionPriority,
    DateOnly? ValueDate,
    string Currency,
    decimal Amount,
    IncomingAgent DebtorAgent,
    IncomingAgent CreditorAgent,
    IncomingCode? CategoryPurpose,
    string? Purpose,
    string? AdditionalPurpose,
    string? DebtorAccount,
    string? CreditorAccount) : IIncomingTransferContent
{
    string IIncomingTransferContent.Kind => PaymentMessageTypes.Pacs009;

    string IIncomingTransferContent.Key => EndToEndId;

    string IIncomingTransferContent.ReceiverBic => CreditorAgent.Bic;

    CoreIdentifiers IIncomingTransferContent.Echo => new(EndToEndId, MessageId, TransactionId);
}

public sealed record IncomingAgent(string Bic, string? ClearingSystemMemberId);

// Type 0 is an ISO code, type 1 a proprietary value.
public sealed record IncomingCode(int Type, string Value);
