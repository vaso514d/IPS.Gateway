using IPS.Middleware.Infrastructure.Persistence.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static IPS.Middleware.Infrastructure.Persistence.Events.EventRegistry;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class TransactionEventConfiguration : IEntityTypeConfiguration<TransactionEventRow>
{
    public void Configure(EntityTypeBuilder<TransactionEventRow> builder)
    {
        builder.ToTable("TransactionEvents", table => table.HasCheckConstraint(
            "CK_TransactionEvents_Kind",
            $"([AggregateKind] = '{OutgoingPaymentKind}' AND [Name] LIKE 'payment.%') OR " +
            $"([AggregateKind] = '{IncomingPaymentKind}' AND [Name] LIKE 'incoming-payment.%') OR " +
            $"([AggregateKind] = '{IncomingTransferKind}' AND [Name] LIKE 'incoming-transfer.%')"));
        builder.HasKey(x => new { x.TransactionId, x.Sequence });
        builder.HasIndex(x => x.EventId).IsUnique();
        builder.Property(x => x.EventId).ValueGeneratedNever();
        builder.Property(x => x.AggregateKind).HasMaxLength(AggregateIdentity.KindLength).IsUnicode(false);
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.PayloadJson).IsRequired();

        builder.HasOne<AggregateIdentity>()
            .WithMany()
            .HasForeignKey(x => new { x.TransactionId, x.AggregateKind })
            .HasPrincipalKey(x => new { x.Id, x.Kind })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
