using IPS.Middleware.Infrastructure.Persistence.Events;
using IPS.Middleware.Infrastructure.Persistence.History;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class TransferStatusConfiguration : IEntityTypeConfiguration<TransferStatusRow>
{
    public void Configure(EntityTypeBuilder<TransferStatusRow> builder)
    {
        builder.ToTable("PaymentTransactionStatuses");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityColumn();
        builder.Property(x => x.MessageType).HasMaxLength(TransferStatusRow.MessageTypeLength).IsUnicode(false);
        builder.Property(x => x.ReasonCode).HasMaxLength(TransferStatusRow.ReasonCodeLength);
        builder.Property(x => x.Description).HasMaxLength(TransferStatusRow.DescriptionLength);
        builder.HasIndex(x => new { x.PaymentTransactionId, x.Id });
        builder.HasIndex(x => new { x.Direction, x.AtUtc });

        builder.HasOne<AggregateIdentity>()
            .WithMany()
            .HasForeignKey(x => x.PaymentTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
