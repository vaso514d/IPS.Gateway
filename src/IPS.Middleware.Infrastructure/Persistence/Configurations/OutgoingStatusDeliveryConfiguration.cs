using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class OutgoingStatusDeliveryConfiguration : IEntityTypeConfiguration<OutgoingStatusDeliveryRow>
{
    public void Configure(EntityTypeBuilder<OutgoingStatusDeliveryRow> row)
    {
        row.ToTable("OutgoingStatusDeliveries", table =>
        {
            table.HasCheckConstraint("CK_OutgoingStatusDeliveries_Payload", "[Sequence] > 0 AND [PayloadVersion] = 1 AND ISJSON([PayloadJson]) = 1 AND [Attempts] >= 0");
            table.HasCheckConstraint("CK_OutgoingStatusDeliveries_State",
                "([State] = 0 AND [NextAtUtc] IS NOT NULL AND [DeliveredAtUtc] IS NULL) OR " +
                "([State] = 1 AND [NextAtUtc] IS NULL AND [DeliveredAtUtc] IS NOT NULL AND [ClaimToken] IS NULL) OR " +
                "([State] = 2 AND [NextAtUtc] IS NULL AND [DeliveredAtUtc] IS NULL AND [ClaimToken] IS NULL)");
            table.HasCheckConstraint("CK_OutgoingStatusDeliveries_Claim",
                "([ClaimToken] IS NULL AND [ClaimExpiresAtUtc] IS NULL) OR ([ClaimToken] IS NOT NULL AND [ClaimExpiresAtUtc] IS NOT NULL AND [Attempts] > 0 AND [State] = 0)");
        });
        row.HasKey(p => new { p.PaymentId, p.Sequence });
        row.Property(p => p.LastFailure).HasMaxLength(2000);
        row.Property(p => p.RowVersion).IsRowVersion();
        row.HasIndex(p => new { p.State, p.NextAtUtc, p.PaymentId, p.Sequence });
        row.HasOne<OutgoingPayment>().WithMany().HasForeignKey(p => p.PaymentId).OnDelete(DeleteBehavior.NoAction);
    }
}
