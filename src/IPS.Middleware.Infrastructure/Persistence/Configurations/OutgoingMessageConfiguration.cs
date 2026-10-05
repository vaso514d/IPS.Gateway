using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class OutgoingMessageConfiguration : IEntityTypeConfiguration<OutgoingMessageRow>
{
    public void Configure(EntityTypeBuilder<OutgoingMessageRow> row)
    {
        row.ToTable("OutgoingMessages", table =>
        {
            table.HasCheckConstraint("CK_OutgoingMessages_Lifecycle",
                "([Direction] = 0 AND [MessageDefinition] IS NOT NULL AND (([InvestigationId] IS NULL AND [MessageDefinition] = 'pacs.008.001.12' AND [OriginatingMessageId] IS NULL) OR ([InvestigationId] IS NOT NULL AND [MessageDefinition] = 'pacs.028.001.06' AND [OriginatingMessageId] IS NOT NULL)) AND [Disposition] IS NOT NULL AND [Disposition] IN (0,1) " +
                "AND [HttpStatusCode] IS NULL AND [HeadersJson] IS NULL AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL " +
                "AND (([Status] = 0 AND [StartedAtUtc] IS NULL AND [SubmissionOwner] IS NULL) OR " +
                "([Status] = 1 AND [StartedAtUtc] IS NOT NULL AND [SubmissionOwner] IS NOT NULL))) OR " +
                "([Direction] = 1 AND [OriginatingMessageId] IS NOT NULL AND [Disposition] IS NULL AND [StartedAtUtc] IS NULL " +
                "AND [SubmissionOwner] IS NULL AND [HttpStatusCode] IS NOT NULL AND [HttpStatusCode] BETWEEN 100 AND 599 AND [HeadersJson] IS NOT NULL AND ISJSON([HeadersJson]) = 1 " +
                "AND (([Status] = 2 AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL) OR " +
                "([Status] = 3 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NULL) OR " +
                "([Status] = 4 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NOT NULL)))");
        });
        row.HasKey(p => p.Id);
        row.Property(p => p.Id).ValueGeneratedNever();
        row.Property(p => p.MessageDefinition).HasMaxLength(35);
        row.Property(p => p.Failure).HasMaxLength(2000);
        row.HasIndex(p => new { p.PaymentId, p.Direction }).IsUnique().HasFilter("[InvestigationId] IS NULL");
        row.HasIndex(p => new { p.InvestigationId, p.Direction }).IsUnique().HasFilter("[InvestigationId] IS NOT NULL");
        row.HasOne<InvestigationRow>().WithMany().HasForeignKey(p => new { p.InvestigationId, p.PaymentId })
            .HasPrincipalKey(p => new { p.Id, p.PaymentId }).OnDelete(DeleteBehavior.NoAction);
        row.HasAlternateKey(p => new { p.Id, p.PaymentId });
        row.HasOne<OutgoingPayment>().WithMany().HasForeignKey(p => p.PaymentId).OnDelete(DeleteBehavior.NoAction);
        row.HasOne<OutgoingMessageRow>().WithMany().HasForeignKey(p => new { p.OriginatingMessageId, p.PaymentId })
            .HasPrincipalKey(p => new { p.Id, p.PaymentId }).OnDelete(DeleteBehavior.NoAction);
    }
}
