using System.Collections.ObjectModel;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Inbound.Pacs008;

public sealed class IncomingPacs008(Pacs008Request payment, IncomingPacs008Reference original)
{
    public Pacs008Request Payment { get; } = Freeze(payment);
    public IncomingPacs008Reference Original { get; } = original;

    /// <summary>
    /// Same frozen contents: ordinal strings, numeric amounts, timestamp instants, ordered lists; null differs from empty.
    /// The argument is frozen too, so the comparison stays structural whatever list types the caller supplies.
    /// </summary>
    public bool HasSameContents(Pacs008Request stored) => Payment == Freeze(stored);
    /// <summary>
    /// Callers may keep the mutable lists they passed in. The copy holds read-only lists compared by ordered contents,
    /// so the explicit value comparison includes every field and ordered collection.
    /// </summary>
    public static Pacs008Request Freeze(Pacs008Request payment) => new Pacs008Request(payment)
    {
        PaymentInitiation = payment.PaymentInitiation is { } initiation ? new Pacs008PaymentInitiationInput(initiation)
        {
            Geolocation = Copy(initiation.Geolocation)
        } : null,
        InitiationChannelInstrument = payment.InitiationChannelInstrument is { } channel ? new Pacs008InitiationChannelInstrumentInput(channel)
        {
            InstrumentCodes = Copy(channel.InstrumentCodes)
        } : null,
        Remittance = payment.Remittance is { } remittance ? new Pacs008RemittanceInput(remittance)
        {
            Structured = Copy(remittance.Structured)
        } : null
    };
    private static ValueList<T>? Copy<T>(IReadOnlyList<T>? values) => values is null ? null : new(values.ToArray());
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

public sealed class IncomingPacs008Reference : IEquatable<IncomingPacs008Reference>
{
    [System.Text.Json.Serialization.JsonConstructor]
    public IncomingPacs008Reference(
        string businessMessageId,
        string groupMessageId,
        string endToEndId,
        string? transactionId,
        Guid? uetr,
        DateTimeOffset? groupCreatedAtUtc,
        DateOnly? settlementDate,
        string? debtorAgentBic,
        string? serviceLevelCode,
        string? localInstrumentCode)
    {
        BusinessMessageId = businessMessageId;
        GroupMessageId = groupMessageId;
        EndToEndId = endToEndId;
        TransactionId = transactionId;
        Uetr = uetr;
        GroupCreatedAtUtc = groupCreatedAtUtc;
        SettlementDate = settlementDate;
        DebtorAgentBic = debtorAgentBic;
        ServiceLevelCode = serviceLevelCode;
        LocalInstrumentCode = localInstrumentCode;
    }

    public string BusinessMessageId { get; init; }
    public string GroupMessageId { get; init; }
    public string EndToEndId { get; init; }
    public string? TransactionId { get; init; }
    public Guid? Uetr { get; init; }
    public DateTimeOffset? GroupCreatedAtUtc { get; init; }
    public DateOnly? SettlementDate { get; init; }
    public string? DebtorAgentBic { get; init; }
    public string? ServiceLevelCode { get; init; }
    public string? LocalInstrumentCode { get; init; }

    public IncomingPacs008Reference(IncomingPacs008Reference original)
    {
        BusinessMessageId = original.BusinessMessageId;
        GroupMessageId = original.GroupMessageId;
        EndToEndId = original.EndToEndId;
        TransactionId = original.TransactionId;
        Uetr = original.Uetr;
        GroupCreatedAtUtc = original.GroupCreatedAtUtc;
        SettlementDate = original.SettlementDate;
        DebtorAgentBic = original.DebtorAgentBic;
        ServiceLevelCode = original.ServiceLevelCode;
        LocalInstrumentCode = original.LocalInstrumentCode;
    }

    public bool Equals(IncomingPacs008Reference? other) => other is not null &&
            Equals(BusinessMessageId, other.BusinessMessageId) &&
            Equals(GroupMessageId, other.GroupMessageId) &&
            Equals(EndToEndId, other.EndToEndId) &&
            Equals(TransactionId, other.TransactionId) &&
            Equals(Uetr, other.Uetr) &&
            Equals(GroupCreatedAtUtc, other.GroupCreatedAtUtc) &&
            Equals(SettlementDate, other.SettlementDate) &&
            Equals(DebtorAgentBic, other.DebtorAgentBic) &&
            Equals(ServiceLevelCode, other.ServiceLevelCode) &&
            Equals(LocalInstrumentCode, other.LocalInstrumentCode);
    public override bool Equals(object? obj) => obj is IncomingPacs008Reference other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(BusinessMessageId);
        hash.Add(GroupMessageId);
        hash.Add(EndToEndId);
        hash.Add(TransactionId);
        hash.Add(Uetr);
        hash.Add(GroupCreatedAtUtc);
        hash.Add(SettlementDate);
        hash.Add(DebtorAgentBic);
        hash.Add(ServiceLevelCode);
        hash.Add(LocalInstrumentCode);
        return hash.ToHashCode();
    }

    public static bool operator ==(IncomingPacs008Reference? left, IncomingPacs008Reference? right) => Equals(left, right);
    public static bool operator !=(IncomingPacs008Reference? left, IncomingPacs008Reference? right) => !Equals(left, right);
}

public abstract class IncomingPacs008ReadResult
{
    public sealed class Ready : IncomingPacs008ReadResult
    {
        public Ready(IncomingPacs008 payment)
        {
            Payment = payment;
        }

        public IncomingPacs008 Payment { get; init; }
    }

    public sealed class Reject : IncomingPacs008ReadResult
    {
        public Reject(IncomingPacs008Reference original, string reasonCode, string description)
        {
            Original = original;
            ReasonCode = reasonCode;
            Description = description;
        }

        public IncomingPacs008Reference Original { get; init; }
        public string ReasonCode { get; init; }
        public string Description { get; init; }
    }

    public sealed class Hold : IncomingPacs008ReadResult
    {
        public Hold(string reason)
        {
            Reason = reason;
        }

        public string Reason { get; init; }
    }
}

/// <summary>Values must be persisted once by the workflow, then reused for every preparation attempt.</summary>
public sealed class IncomingReplyContext
{
    public IncomingReplyContext(string messageId, string statusId, DateTimeOffset createdAtUtc)
    {
        MessageId = messageId;
        StatusId = statusId;
        CreatedAtUtc = createdAtUtc;
    }

    public string MessageId { get; init; }
    public string StatusId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class IncomingReplyDecision : IEquatable<IncomingReplyDecision>
{
    public IncomingReplyDecision(bool accepted, DateTimeOffset processedAtUtc, string? reasonCode = null, string? description = null)
    {
        Accepted = accepted;
        ProcessedAtUtc = processedAtUtc;
        ReasonCode = reasonCode;
        Description = description;
    }

    public bool Accepted { get; init; }
    public DateTimeOffset ProcessedAtUtc { get; init; }
    public string? ReasonCode { get; init; }
    public string? Description { get; init; }

    public bool Equals(IncomingReplyDecision? other) => other is not null &&
            Equals(Accepted, other.Accepted) &&
            Equals(ProcessedAtUtc, other.ProcessedAtUtc) &&
            Equals(ReasonCode, other.ReasonCode) &&
            Equals(Description, other.Description);
    public override bool Equals(object? obj) => obj is IncomingReplyDecision other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Accepted);
        hash.Add(ProcessedAtUtc);
        hash.Add(ReasonCode);
        hash.Add(Description);
        return hash.ToHashCode();
    }

    public static bool operator ==(IncomingReplyDecision? left, IncomingReplyDecision? right) => Equals(left, right);
    public static bool operator !=(IncomingReplyDecision? left, IncomingReplyDecision? right) => !Equals(left, right);
}
