using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace IPS.Middleware.Infrastructure.Persistence;

internal static class PaymentOwnership
{
    internal static bool HasLiveClaim(EntityEntry<OutgoingPayment> entry, TransactionClaim claim, DateTimeOffset now) =>
        claim.Token != Guid.Empty && claim.TransactionId == entry.Entity.Id &&
        entry.Property<Guid?>("ClaimToken").CurrentValue == claim.Token &&
        entry.Property<DateTimeOffset?>("ClaimExpiresAtUtc").CurrentValue is { } expiry && expiry > now;
}
