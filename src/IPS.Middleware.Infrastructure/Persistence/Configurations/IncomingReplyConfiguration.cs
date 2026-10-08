using IPS.Middleware.Infrastructure.Persistence.Inbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class IncomingReplyConfiguration : IEntityTypeConfiguration<IncomingReplyRow>
{
    public void Configure(EntityTypeBuilder<IncomingReplyRow> builder)
    {
        builder.ToTable("IncomingReplies", table =>
        {
            table.HasCheckConstraint("CK_IncomingReplies_Envelope", "ISJSON([EnvelopeJson]) = 1");
            table.HasCheckConstraint("CK_IncomingReplies_Status", "[Status] IN (0,1,2,3)");
            table.HasCheckConstraint("CK_IncomingReplies_Message",
                "([MessageXml] IS NULL AND [MessageKind] IS NULL AND [Status] = 0) OR " +
                "([UnsignedXml] IS NOT NULL AND [MessageXml] IS NOT NULL AND [MessageKind] IN (0,1) AND [Status] IN (1,2,3))");
            table.HasCheckConstraint("CK_IncomingReplies_Review",
                "([Status] = 3 AND [ReviewReason] IS NOT NULL) OR ([Status] <> 3 AND [ReviewReason] IS NULL)");
        });
        builder.HasKey(x => x.JournalId);

        builder.HasOne<InboundJournalEntry>()
            .WithOne()
            .HasForeignKey<IncomingReplyRow>(x => x.JournalId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IncomingReplyAttemptConfiguration : IEntityTypeConfiguration<IncomingReplyAttemptRow>
{
    public void Configure(EntityTypeBuilder<IncomingReplyAttemptRow> builder)
    {
        builder.ToTable("IncomingReplyAttempts", table =>
        {
            table.HasCheckConstraint("CK_IncomingReplyAttempts_Number", "[Number] > 0");
            table.HasCheckConstraint("CK_IncomingReplyAttempts_Completion",
                "([CompletionJson] IS NULL AND [Consumed] = 0) OR ([CompletionJson] IS NOT NULL AND ISJSON([CompletionJson]) = 1)");
        });
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.JournalId, x.Number }).IsUnique();

        builder.HasOne<IncomingReplyRow>()
            .WithMany()
            .HasForeignKey(x => x.JournalId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
