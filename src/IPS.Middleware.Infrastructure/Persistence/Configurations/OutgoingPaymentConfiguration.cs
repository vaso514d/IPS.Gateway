using IPS.Middleware.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class OutgoingPaymentConfiguration : IEntityTypeConfiguration<OutgoingPayment>
{
    public void Configure(EntityTypeBuilder<OutgoingPayment> payment)
    {
        payment.ToTable("Transactions");
        payment.HasKey(p => p.Id);
        payment.Property(p => p.Id).ValueGeneratedNever();
        payment.Property(p => p.MessageType).HasMaxLength(16);
        payment.Property(p => p.ClientReference).HasMaxLength(35);
        payment.HasIndex(p => p.ClientReference).IsUnique();
        payment.Property(p => p.EventSequence).HasColumnName("LastSequence");
        payment.Property(p => p.CurrentReasonCode).HasMaxLength(35);
        payment.Property(p => p.CurrentDescription).HasMaxLength(2000);
        payment.Ignore(p => p.PendingEvents);
        payment.Ignore(p => p.Current);
        payment.Ignore(p => p.IsFinal);
        payment.Property<string>("RequestJson").IsRequired();
        payment.Property<TransactionDirection>("Direction");
        payment.Property<Guid?>("ClaimToken");
        payment.Property<DateTimeOffset?>("ClaimExpiresAtUtc");
        payment.Property<DateTimeOffset?>("NextActionAtUtc");
        payment.Property<byte[]>("RowVersion").IsRequired().IsRowVersion();
        payment.HasIndex("Direction", nameof(OutgoingPayment.CurrentStatus), nameof(OutgoingPayment.MessageType),
            nameof(OutgoingPayment.CurrentStatusAtUtc), nameof(OutgoingPayment.Id));
        payment.HasIndex("ClaimExpiresAtUtc");
        payment.ToTable(table => table.HasCheckConstraint("CK_Transactions_Claim",
            "([ClaimToken] IS NULL AND [ClaimExpiresAtUtc] IS NULL) OR ([ClaimToken] IS NOT NULL AND [ClaimExpiresAtUtc] IS NOT NULL)"));

    }
}
