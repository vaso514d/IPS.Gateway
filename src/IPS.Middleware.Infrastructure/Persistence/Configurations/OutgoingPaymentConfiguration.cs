using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static IPS.Middleware.Infrastructure.Persistence.Events.EventRegistry;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class OutgoingPaymentConfiguration : IEntityTypeConfiguration<OutgoingPayment>
{
    public void Configure(EntityTypeBuilder<OutgoingPayment> payment)
    {
        payment.ToTable("Transactions");
        payment.HasKey(p => p.Id);
        payment.Property(p => p.Id).ValueGeneratedNever();
        payment.HasAggregateIdentity(OutgoingPaymentKind);
        payment.Property(p => p.MessageType).HasMaxLength(16);
        payment.Property(p => p.ClientReference).HasMaxLength(35);
        payment.HasIndex(p => p.ClientReference).IsUnique();
        payment.Property(p => p.EventSequence).HasColumnName("LastSequence");
        payment.Property(p => p.CurrentReasonCode).HasMaxLength(35);
        payment.Property(p => p.CurrentDescription).HasMaxLength(2000);
        payment.Ignore(p => p.PendingEvents);
        payment.Ignore(p => p.Current);
        payment.Ignore(p => p.IsFinal);
        payment.Property<byte[]>(RowVersion).IsRequired().IsRowVersion();
        payment.HasIndex(p => new { p.CurrentStatus, p.MessageType, p.CurrentStatusAtUtc, p.Id });
    }
}
