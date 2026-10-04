using IPS.Middleware.Infrastructure.Persistence.Inbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class IncomingReplyConfiguration : IEntityTypeConfiguration<IncomingReplyRow>
{
    public void Configure(EntityTypeBuilder<IncomingReplyRow> b)
    {
        b.ToTable("IncomingReplies", t =>
        {
            t.HasCheckConstraint("CK_IncomingReplies_Envelope", "ISJSON([EnvelopeJson]) = 1");
            t.HasCheckConstraint("CK_IncomingReplies_Status", "[Status] IN (0,1,2,3)");
            t.HasCheckConstraint("CK_IncomingReplies_Message", "([MessageXml] IS NULL AND [MessageKind] IS NULL AND [Status] = 0) OR ([UnsignedXml] IS NOT NULL AND [MessageXml] IS NOT NULL AND [MessageKind] IN (0,1) AND [Status] IN (1,2,3))");
            t.HasCheckConstraint("CK_IncomingReplies_Review", "([Status] = 3 AND [ReviewReason] IS NOT NULL) OR ([Status] <> 3 AND [ReviewReason] IS NULL)");
        });
        b.HasKey(e => e.JournalId);
        b.HasOne<InboundJournalEntry>().WithOne().HasForeignKey<IncomingReplyRow>(e => e.JournalId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class IncomingReplyAttemptConfiguration : IEntityTypeConfiguration<IncomingReplyAttemptRow>
{
    public void Configure(EntityTypeBuilder<IncomingReplyAttemptRow> b)
    {
        b.ToTable("IncomingReplyAttempts", t =>
        {
            t.HasCheckConstraint("CK_IncomingReplyAttempts_Number", "[Number] > 0");
            t.HasCheckConstraint("CK_IncomingReplyAttempts_Completion", "([CompletionJson] IS NULL AND [Consumed] = 0) OR ([CompletionJson] IS NOT NULL AND ISJSON([CompletionJson]) = 1)");
        });
        b.HasKey(e => e.Id);
        b.HasIndex(e => new { e.JournalId, e.Number }).IsUnique();
        b.HasOne<IncomingReplyRow>().WithMany().HasForeignKey(e => e.JournalId).OnDelete(DeleteBehavior.Restrict);
    }
}
