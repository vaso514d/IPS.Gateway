using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Inbound.Pacs008;

public sealed class IncomingPacs008(Pacs008Request payment, IncomingPacs008Reference original)
{
    // Callers may keep the mutable lists they passed in; the snapshot holds read-only copies.
    public Pacs008Request Payment { get; } = payment with
    {
        PaymentInitiation = payment.PaymentInitiation is { } initiation ? initiation with { Geolocation = Copy(initiation.Geolocation) } : null,
        InitiationChannelInstrument = payment.InitiationChannelInstrument is { } channel
            ? channel with { InstrumentCodes = Copy(channel.InstrumentCodes) } : null,
        Remittance = payment.Remittance is { } remittance ? remittance with { Structured = Copy(remittance.Structured) } : null
    };

    public IncomingPacs008Reference Original { get; } = original;

    private static IReadOnlyList<T>? Copy<T>(IReadOnlyList<T>? values) => values is null ? null : Array.AsReadOnly(values.ToArray());
}

public sealed record IncomingPacs008Reference(string BusinessMessageId, string GroupMessageId, string EndToEndId,
    string? TransactionId, Guid? Uetr, DateTimeOffset? GroupCreatedAtUtc, DateOnly? SettlementDate,
    string? DebtorAgentBic, string? ServiceLevelCode, string? LocalInstrumentCode);

public abstract record IncomingPacs008ReadResult
{
    public sealed record Ready(IncomingPacs008 Payment) : IncomingPacs008ReadResult;
    public sealed record Reject(IncomingPacs008Reference Original, string ReasonCode, string Description) : IncomingPacs008ReadResult;
    public sealed record Hold(string Reason) : IncomingPacs008ReadResult;
}

/// <summary>Values must be persisted once by the workflow, then reused for every preparation attempt.</summary>
public sealed record IncomingReplyContext(string MessageId, string StatusId, DateTimeOffset CreatedAtUtc);

public sealed record IncomingReplyDecision(bool Accepted, DateTimeOffset ProcessedAtUtc, string? ReasonCode = null, string? Description = null);
