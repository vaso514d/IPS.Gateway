using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Domain.Inbound;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence.Inbound;

/// <summary>Incoming payment shadow columns and typed claim access. Column names shared with outgoing payments stay in PaymentColumns.</summary>
internal static class IncomingPaymentColumns
{
    internal const string ContextJson = nameof(ContextJson);
    internal const string CheckpointVersion = nameof(CheckpointVersion);
    internal const string ReconciliationDeadlineUtc = nameof(ReconciliationDeadlineUtc);
    internal const string FollowUpAtUtc = nameof(FollowUpAtUtc);

    internal static bool HasLiveClaim(this EntityEntry<IncomingPayment> entry, IncomingPaymentClaim claim, DateTimeOffset now) =>
        entry.Property<Guid?>(ClaimToken).CurrentValue == claim.Token &&
        entry.Property<DateTimeOffset?>(ClaimExpiresAtUtc).CurrentValue is { } expiry && expiry > now;

    internal static void SetClaim(this EntityEntry<IncomingPayment> entry, Guid? token, DateTimeOffset? expiresAtUtc)
    {
        entry.Property<Guid?>(ClaimToken).CurrentValue = token;
        entry.Property<DateTimeOffset?>(ClaimExpiresAtUtc).CurrentValue = expiresAtUtc;
    }
}
