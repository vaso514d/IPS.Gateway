using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class OutgoingMessageConfiguration : IEntityTypeConfiguration<OutgoingMessageRow>
{
    public void Configure(EntityTypeBuilder<OutgoingMessageRow> builder)
    {
        builder.ToTable("OutgoingMessages", table =>
        {
            table.HasCheckConstraint("CK_OutgoingMessages_Lifecycle",
                "([InvestigationId] IS NULL OR [ResendId] IS NULL) AND (" +
                "([Direction] = 0 AND [MessageDefinition] IS NOT NULL AND (" +
                "([InvestigationId] IS NULL AND [ResendId] IS NULL AND [MessageDefinition] IN ('pacs.008.001.12', 'pacs.009.001.11', 'pacs.004.001.13') AND [OriginatingMessageId] IS NULL) OR " +
                "([InvestigationId] IS NOT NULL AND [MessageDefinition] = 'pacs.028.001.06' AND [OriginatingMessageId] IS NOT NULL) OR " +
                "([ResendId] IS NOT NULL AND [MessageDefinition] IN ('pacs.008.001.12', 'pacs.009.001.11', 'pacs.004.001.13') AND [OriginatingMessageId] IS NOT NULL)) " +
                "AND [Disposition] IS NOT NULL AND [Disposition] IN (0,1) " +
                "AND [HttpStatusCode] IS NULL AND [HeadersJson] IS NULL AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL " +
                "AND (([Status] = 0 AND [StartedAtUtc] IS NULL AND [SubmissionOwner] IS NULL) OR " +
                "([Status] = 1 AND [StartedAtUtc] IS NOT NULL AND [SubmissionOwner] IS NOT NULL))) OR " +
                "([Direction] = 1 AND [OriginatingMessageId] IS NOT NULL AND [Disposition] IS NULL AND [StartedAtUtc] IS NULL " +
                "AND [SubmissionOwner] IS NULL AND [HttpStatusCode] IS NOT NULL AND [HttpStatusCode] BETWEEN 100 AND 599 AND [HeadersJson] IS NOT NULL AND ISJSON([HeadersJson]) = 1 " +
                "AND (([Status] = 2 AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL) OR " +
                "([Status] = 3 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NULL) OR " +
                "([Status] = 4 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NOT NULL))))");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasAlternateKey(x => new { x.Id, x.PaymentId });
        builder.Property(x => x.MessageDefinition).HasMaxLength(35);
        builder.Property(x => x.Failure).HasMaxLength(2000);
        builder.HasIndex(x => new { x.PaymentId, x.Direction }).IsUnique().HasFilter("[InvestigationId] IS NULL AND [ResendId] IS NULL");
        builder.HasIndex(x => new { x.InvestigationId, x.Direction }).IsUnique().HasFilter("[InvestigationId] IS NOT NULL");
        builder.HasIndex(x => new { x.ResendId, x.Direction }).IsUnique().HasFilter("[ResendId] IS NOT NULL");

        builder.HasOne<OutgoingPayment>()
            .WithMany()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<InvestigationRow>()
            .WithMany()
            .HasForeignKey(x => new { x.InvestigationId, x.PaymentId })
            .HasPrincipalKey(x => new { x.Id, x.PaymentId })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<ResendRow>()
            .WithMany()
            .HasForeignKey(x => new { x.ResendId, x.PaymentId })
            .HasPrincipalKey(x => new { x.Id, x.PaymentId })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<OutgoingMessageRow>()
            .WithMany()
            .HasForeignKey(x => new { x.OriginatingMessageId, x.PaymentId })
            .HasPrincipalKey(x => new { x.Id, x.PaymentId })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
