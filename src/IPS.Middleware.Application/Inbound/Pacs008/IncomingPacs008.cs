using System.Collections.ObjectModel;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Inbound.Pacs008;

public sealed class IncomingPacs008(Pacs008Request payment, IncomingPacs008Reference original)
{
    public Pacs008Request Payment { get; } = Freeze(payment);
    public IncomingPacs008Reference Original { get; } = original;

    // Same frozen contents: ordinal strings, numeric amounts, timestamp instants, ordered lists; null differs from empty.
    // The argument is frozen too, so the comparison stays structural whatever list types the caller supplies.
    public bool HasSameContents(Pacs008Request stored) => Payment == Freeze(stored);

    // Callers may keep the lists they passed in; the frozen copy compares lists by ordered contents.
    public static Pacs008Request Freeze(Pacs008Request payment) => payment with
    {
        PaymentInitiation = payment.PaymentInitiation is { } initiation
            ? initiation with { Geolocation = Copy(initiation.Geolocation) }
            : null,
        InitiationChannelInstrument = payment.InitiationChannelInstrument is { } channel
            ? channel with { InstrumentCodes = Copy(channel.InstrumentCodes) }
            : null,
        Remittance = payment.Remittance is { } remittance
            ? remittance with { Structured = Copy(remittance.Structured) }
            : null
    };

    private static ValueList<T>? Copy<T>(IReadOnlyList<T>? values) => values is null ? null : new ValueList<T>(values.ToArray());

    private sealed class ValueList<T>(T[] values) : ReadOnlyCollection<T>(values)
    {
        public override bool Equals(object? obj) => obj is ValueList<T> other && this.SequenceEqual(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var value in this)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }
    }
}

public sealed record IncomingPacs008Reference(
    string BusinessMessageId,
    string GroupMessageId,
    string EndToEndId,
    string? TransactionId,
    Guid? Uetr,
    DateTimeOffset? GroupCreatedAtUtc,
    DateOnly? SettlementDate,
    string? DebtorAgentBic,
    string? ServiceLevelCode,
    string? LocalInstrumentCode);

public abstract record IncomingPacs008ReadResult
{
    public sealed record Ready(IncomingPacs008 Payment) : IncomingPacs008ReadResult;

    public sealed record Reject(IncomingPacs008Reference Original, string ReasonCode, string Description) : IncomingPacs008ReadResult;

    public sealed record Hold(string Reason) : IncomingPacs008ReadResult;
}

// Persisted once by the workflow, then reused for every preparation attempt.
public sealed record IncomingReplyContext(string MessageId, string StatusId, DateTimeOffset CreatedAtUtc);

public sealed record IncomingReplyDecision(bool Accepted, DateTimeOffset ProcessedAtUtc, string? ReasonCode = null, string? Description = null);
