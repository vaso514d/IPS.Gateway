using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static IPS.Middleware.Infrastructure.Persistence.Events.EventRegistry;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class OutgoingPaymentConfiguration : IEntityTypeConfiguration<OutgoingPayment>
{
    public void Configure(EntityTypeBuilder<OutgoingPayment> builder)
    {
        builder.ToTable("Transactions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasAggregateIdentity(OutgoingPaymentKind);
        builder.Property(x => x.MessageType).HasMaxLength(16);
        builder.Property(x => x.ClientReference).HasMaxLength(35);
        builder.HasIndex(x => x.ClientReference).IsUnique();
        builder.Property(x => x.EventSequence).HasColumnName("LastSequence");
        builder.Property(x => x.CurrentReasonCode).HasMaxLength(35);
        builder.Property(x => x.CurrentDescription).HasMaxLength(2000);
        builder.Property<byte[]>(RowVersion).IsRequired().IsRowVersion();
        // Shared with the metadata's read-only copies, which EF Core allows only for explicitly named columns.
        builder.Property(x => x.CurrentStatus).HasColumnName(nameof(OutgoingPayment.CurrentStatus));
        builder.Property(x => x.CurrentStatusAtUtc).HasColumnName(nameof(OutgoingPayment.CurrentStatusAtUtc));
        builder.Ignore(x => x.PendingEvents);
        builder.Ignore(x => x.Current);
        builder.Ignore(x => x.IsFinal);
    }
}

// Technical state shares the payment's row, so ownership and protocol writes are fenced by the same row version.
internal sealed class OutgoingPaymentMetadataConfiguration : IEntityTypeConfiguration<OutgoingPaymentMetadata>
{
    public void Configure(EntityTypeBuilder<OutgoingPaymentMetadata> builder)
    {
        builder.ToTable("Transactions", table =>
        {
            table.HasCheckConstraint("CK_Transactions_Accepted",
                "[AcceptedJson] IS NULL OR ([MessageType] IN ('pacs.008', 'pacs.009', 'pacs.004', 'camt.056', 'camt.029', 'pain.002') AND ISJSON([AcceptedJson]) = 1)");
            table.HasCheckConstraint("CK_Transactions_Preparation",
                "([MessageId] IS NULL AND [ProtocolTransactionId] IS NULL AND [UnsignedXml] IS NULL) OR " +
                "([MessageType] IN ('pacs.008', 'pacs.009', 'pacs.004', 'camt.056', 'camt.029', 'pain.002') AND [MessageId] IS NOT NULL AND [ProtocolTransactionId] IS NOT NULL)");
            table.HasCheckConstraint("CK_Transactions_Claim",
                "([ClaimToken] IS NULL AND [ClaimExpiresAtUtc] IS NULL) OR ([ClaimToken] IS NOT NULL AND [ClaimExpiresAtUtc] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RequestJson).IsRequired();
        builder.Property(x => x.MessageId).HasMaxLength(35);
        builder.Property(x => x.ProtocolTransactionId).HasMaxLength(35);
        builder.Property(x => x.RowVersion).IsRequired().IsRowVersion();
        builder.HasIndex(x => x.MessageId).IsUnique().HasFilter("[MessageId] IS NOT NULL");
        builder.HasIndex(x => x.ProtocolTransactionId).IsUnique().HasFilter("[ProtocolTransactionId] IS NOT NULL");
        // Discovery reads one index without a lookup: due work in dispatch order (pacs.008 first, then the oldest status change),
        // expired claims, and the scheduled work the backlog gauge counts. A lookup would hold the index row while waiting for a
        // claimed payment's row, the order in which the 013 baseline deadlocked delivery discovery.
        builder.Property(x => x.DispatchPriority).HasComputedColumnSql($"CASE WHEN [MessageType] = N'{Pacs008}' THEN 0 ELSE 1 END", stored: true);
        ReadOnly(builder.Property(x => x.CurrentStatus).HasColumnName(nameof(OutgoingPayment.CurrentStatus)));
        ReadOnly(builder.Property(x => x.CurrentStatusAtUtc).HasColumnName(nameof(OutgoingPayment.CurrentStatusAtUtc)));
        builder.HasIndex(x => new { x.CurrentStatus, x.DispatchPriority, x.CurrentStatusAtUtc, x.Id })
            .IncludeProperties(x => new { x.ClaimToken, x.NextActionAtUtc });
        builder.HasIndex(x => x.ClaimExpiresAtUtc)
            .IncludeProperties(x => new { x.ClaimToken, x.DispatchPriority, x.CurrentStatusAtUtc });
        builder.HasIndex(x => x.NextActionAtUtc).HasFilter("[NextActionAtUtc] IS NOT NULL");

        builder.HasOne(x => x.Payment)
            .WithOne()
            .HasForeignKey<OutgoingPayment>(x => x.Id)
            .IsRequired();
        builder.Navigation(x => x.Payment).IsRequired();
    }

    // A column the payment writes, read here only.
    private static void ReadOnly<T>(PropertyBuilder<T> property)
    {
        property.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        property.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
}
