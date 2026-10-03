using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class TransactionEventConfiguration : IEntityTypeConfiguration<TransactionEventRow>
{
    public void Configure(EntityTypeBuilder<TransactionEventRow> occurrence)
    {
        occurrence.ToTable("TransactionEvents");
        occurrence.HasKey(e => new { e.TransactionId, e.Sequence });
        occurrence.HasIndex(e => e.EventId).IsUnique();
        occurrence.Property(e => e.EventId).ValueGeneratedNever();
        occurrence.Property(e => e.Name).HasMaxLength(100);
        occurrence.Property(e => e.PayloadJson).IsRequired();
        occurrence.HasOne<OutgoingPayment>().WithMany().HasForeignKey(e => e.TransactionId).OnDelete(DeleteBehavior.Restrict);
    }
}
