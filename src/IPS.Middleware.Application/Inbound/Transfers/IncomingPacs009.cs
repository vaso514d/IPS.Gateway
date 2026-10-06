namespace IPS.Middleware.Application.Inbound.Transfers;

// The verified content of a pacs.009 received from IPS, as the core system is told about it. Records compare by value,
// so a redelivery is recognized by equality.
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
    string? CreditorAccount);

public sealed record IncomingAgent(string Bic, string? ClearingSystemMemberId);

// Type 0 is an ISO code, type 1 a proprietary value.
public sealed record IncomingCode(int Type, string Value);

public abstract record IncomingPacs009ReadResult
{
    public sealed record Ready(IncomingPacs009 Transfer) : IncomingPacs009ReadResult;

    public sealed record Hold(string Reason) : IncomingPacs009ReadResult;
}

public interface IIncomingPacs009Protocol
{
    // Ready only for a trusted, schema-valid single transfer with everything the core needs; otherwise Hold. A value date
    // IPS did not send stays absent, so a redelivery on another day is still the same content.
    IncomingPacs009ReadResult Read(string xml);
}
