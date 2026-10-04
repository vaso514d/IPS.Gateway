using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class InboundJournalConfiguration : IEntityTypeConfiguration<InboundJournalEntry>
{
    public void Configure(EntityTypeBuilder<InboundJournalEntry> builder)
    {
        builder.ToTable("InboundMessageJournal", table =>
        {
            table.HasCheckConstraint("CK_InboundJournal_Status", "[Status] IN (0, 1, 2)");
            table.HasCheckConstraint("CK_InboundJournal_Scheduling",
                "([Status] = 0 AND [NextActionAtUtc] IS NOT NULL) OR ([Status] <> 0 AND [NextActionAtUtc] IS NULL AND [ClaimToken] IS NULL)");
            table.HasCheckConstraint("CK_InboundJournal_Claim",
                "([ClaimToken] IS NULL AND [ClaimExpiresAtUtc] IS NULL) OR ([ClaimToken] IS NOT NULL AND [ClaimExpiresAtUtc] IS NOT NULL)");
            // Invalid sequences are always held; valid ones may also be held for protocol failures or identity conflicts.
            table.HasCheckConstraint("CK_InboundJournal_Sequence",
                "([Status] = 2 AND [HoldReason] IS NOT NULL) OR ([Status] IN (0, 1) AND [Sequence] IS NOT NULL AND [Sequence] > 0 AND [HoldReason] IS NULL)");
            table.HasCheckConstraint("CK_InboundJournal_Attachment",
                "([IncomingPaymentId] IS NULL AND [OriginalJson] IS NULL) OR ([OriginalJson] IS NOT NULL AND ISJSON([OriginalJson]) = 1)");
            table.HasCheckConstraint("CK_InboundJournal_Duplicates", "[DuplicateCount] >= 0");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ParticipantBic).HasMaxLength(11).UseCollation(BinaryCollation).IsRequired();
        builder.Property(e => e.MessageType).HasMaxLength(35).IsRequired();
        builder.Property(e => e.RawXml).IsRequired();
        builder.Property(e => e.HoldReason).HasMaxLength(InboundJournalEntry.HoldReasonLimit);
        builder.Property(e => e.Version).IsRowVersion();
        builder.HasOne<IncomingPayment>().WithMany().HasForeignKey(e => e.IncomingPaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.ParticipantBic, e.Sequence }).IsUnique().HasFilter("[Sequence] > 0");
        builder.HasIndex(e => new { e.Status, e.NextActionAtUtc, e.ReceivedAtUtc, e.Id });
    }
}
