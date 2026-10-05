using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace IPS.Middleware.Infrastructure.Persistence;

/// <summary>Infrastructure-only shadow column names, typed access to outgoing ones, and the shared binary identity collation.</summary>
internal static class PaymentColumns
{
    internal const string Pacs008 = "pacs.008";
    internal const string BinaryCollation = "Latin1_General_100_BIN2";

    internal const string RequestJson = nameof(RequestJson);
    internal const string AcceptedJson = nameof(AcceptedJson);
    internal const string Direction = nameof(Direction);
    internal const string MessageId = nameof(MessageId);
    internal const string ProtocolTransactionId = nameof(ProtocolTransactionId);
    internal const string UnsignedXml = nameof(UnsignedXml);
    internal const string ClaimToken = nameof(ClaimToken);
    internal const string ClaimExpiresAtUtc = nameof(ClaimExpiresAtUtc);
    internal const string NextActionAtUtc = nameof(NextActionAtUtc);
    internal const string RowVersion = nameof(RowVersion);

    internal static PropertyEntry<OutgoingPayment, Guid?> ClaimTokenOf(this EntityEntry<OutgoingPayment> entry) =>
        entry.Property<Guid?>(ClaimToken);

    internal static PropertyEntry<OutgoingPayment, DateTimeOffset?> ClaimExpiryOf(this EntityEntry<OutgoingPayment> entry) =>
        entry.Property<DateTimeOffset?>(ClaimExpiresAtUtc);

    internal static PropertyEntry<OutgoingPayment, DateTimeOffset?> NextActionOf(this EntityEntry<OutgoingPayment> entry) =>
        entry.Property<DateTimeOffset?>(NextActionAtUtc);

    internal static PropertyEntry<OutgoingPayment, string?> TextOf(this EntityEntry<OutgoingPayment> entry, string column) =>
        entry.Property<string?>(column);

    internal static bool HasLiveClaim(this EntityEntry<OutgoingPayment> entry, TransactionClaim claim, DateTimeOffset now) =>
        claim.Token != Guid.Empty && claim.TransactionId == entry.Entity.Id &&
        entry.ClaimTokenOf().CurrentValue == claim.Token &&
        entry.ClaimExpiryOf().CurrentValue is { } expiry && expiry > now;
}
