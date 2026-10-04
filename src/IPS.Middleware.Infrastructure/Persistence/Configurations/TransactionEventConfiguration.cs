using IPS.Middleware.Infrastructure.Persistence.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static IPS.Middleware.Infrastructure.Persistence.Events.EventRegistry;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class TransactionEventConfiguration : IEntityTypeConfiguration<TransactionEventRow>
{
    public void Configure(EntityTypeBuilder<TransactionEventRow> occurrence)
    {
        occurrence.ToTable("TransactionEvents", table => table.HasCheckConstraint("CK_TransactionEvents_Kind",
            $"([AggregateKind] = '{OutgoingPaymentKind}' AND [Name] LIKE 'payment.%') OR " +
            $"([AggregateKind] = '{IncomingPaymentKind}' AND [Name] LIKE 'incoming-payment.%')"));
        occurrence.HasKey(e => new { e.TransactionId, e.Sequence });
        occurrence.HasIndex(e => e.EventId).IsUnique();
        occurrence.Property(e => e.EventId).ValueGeneratedNever();
        occurrence.Property(e => e.AggregateKind).HasMaxLength(AggregateIdentity.KindLength).IsUnicode(false);
        occurrence.Property(e => e.Name).HasMaxLength(100);
        occurrence.Property(e => e.PayloadJson).IsRequired();
        occurrence.HasOne<AggregateIdentity>().WithMany()
            .HasForeignKey(e => new { e.TransactionId, e.AggregateKind })
            .HasPrincipalKey(i => new { i.Id, i.Kind })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
