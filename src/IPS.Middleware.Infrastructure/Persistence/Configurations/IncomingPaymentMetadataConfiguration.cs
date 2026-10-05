using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static IPS.Middleware.Infrastructure.Persistence.Events.EventRegistry;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class IncomingPaymentMetadataConfiguration : IEntityTypeConfiguration<IncomingPaymentMetadata>
{
    public void Configure(EntityTypeBuilder<IncomingPaymentMetadata> metadata)
    {
        metadata.ToTable("IncomingPayments");
        metadata.HasKey(p => p.Id);
        metadata.HasOne(p => p.Payment).WithOne().HasForeignKey<IncomingPayment>(p => p.Id).IsRequired();
        metadata.Navigation(p => p.Payment).IsRequired();
        metadata.Property(p => p.Id).ValueGeneratedNever();
        metadata.Property(p => p.RequestJson).IsRequired();
        metadata.Property(p => p.ContextJson).IsRequired();
        metadata.Property(p => p.RowVersion).IsRequired().IsRowVersion();
        metadata.HasIndex(p => new { p.FollowUpAtUtc, p.Id });
        metadata.HasIndex(p => new { p.NextActionAtUtc, p.Id });
        metadata.ToTable(table =>
        {
            table.HasCheckConstraint("CK_IncomingPayments_FollowUpDeadline",
                "[FollowUpAtUtc] IS NULL OR ([ReconciliationDeadlineUtc] IS NOT NULL AND [FollowUpAtUtc] <= [ReconciliationDeadlineUtc])");
            table.HasCheckConstraint("CK_IncomingPayments_Context", "ISJSON([ContextJson]) = 1");
        });
    }
}
