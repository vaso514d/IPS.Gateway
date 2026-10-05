using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static IPS.Middleware.Infrastructure.Persistence.Events.EventRegistry;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class OutgoingPaymentMetadataConfiguration : IEntityTypeConfiguration<OutgoingPaymentMetadata>
{
    public void Configure(EntityTypeBuilder<OutgoingPaymentMetadata> metadata)
    {
        metadata.ToTable("Transactions");
        metadata.HasKey(p => p.Id);
        metadata.HasOne(p => p.Payment).WithOne().HasForeignKey<OutgoingPayment>(p => p.Id).IsRequired();
        metadata.Navigation(p => p.Payment).IsRequired();
        metadata.Property(p => p.Id).ValueGeneratedNever();
        metadata.Property(p => p.RequestJson).IsRequired();
        metadata.Property(p => p.MessageId).HasMaxLength(35);
        metadata.Property(p => p.ProtocolTransactionId).HasMaxLength(35);
        metadata.Property(p => p.RowVersion).IsRequired().IsRowVersion();
        metadata.HasIndex(p => p.MessageId).IsUnique().HasFilter("[MessageId] IS NOT NULL");
        metadata.HasIndex(p => p.ProtocolTransactionId).IsUnique().HasFilter("[ProtocolTransactionId] IS NOT NULL");
        metadata.HasIndex(p => p.ClaimExpiresAtUtc);
        metadata.ToTable(table =>
        {
            table.HasCheckConstraint("CK_Transactions_Accepted",
                "[AcceptedJson] IS NULL OR ([MessageType] = 'pacs.008' AND ISJSON([AcceptedJson]) = 1)");
            table.HasCheckConstraint("CK_Transactions_Preparation",
                "([MessageId] IS NULL AND [ProtocolTransactionId] IS NULL AND [UnsignedXml] IS NULL) OR " +
                "([MessageType] = 'pacs.008' AND [MessageId] IS NOT NULL AND [ProtocolTransactionId] IS NOT NULL )");
            table.HasCheckConstraint("CK_Transactions_Claim",
                "([ClaimToken] IS NULL AND [ClaimExpiresAtUtc] IS NULL) OR ([ClaimToken] IS NOT NULL AND [ClaimExpiresAtUtc] IS NOT NULL)");
        });
    }
}
