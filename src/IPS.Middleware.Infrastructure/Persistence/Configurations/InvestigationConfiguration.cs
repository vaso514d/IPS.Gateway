using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class InvestigationConfiguration : IEntityTypeConfiguration<InvestigationRow>
{
    public void Configure(EntityTypeBuilder<InvestigationRow> builder)
    {
        builder.ToTable("OutgoingInvestigations", table => table.HasCheckConstraint(
            "CK_OutgoingInvestigations_State",
            "[Number] > 0 AND (([Outcome] IS NULL AND [CompletedAtUtc] IS NULL AND [DetailsJson] IS NULL AND [TransportFailure] IS NULL) OR " +
            "([Outcome] IS NOT NULL AND [Outcome] BETWEEN 0 AND 3 AND [CompletedAtUtc] IS NOT NULL AND [DetailsJson] IS NOT NULL AND ISJSON([DetailsJson]) = 1))"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasAlternateKey(x => new { x.Id, x.PaymentId });
        builder.Property(x => x.MessageId).HasMaxLength(35).UseCollation(BinaryCollation);
        builder.Property(x => x.StatusRequestId).HasMaxLength(35).UseCollation(BinaryCollation);
        builder.HasIndex(x => x.MessageId).IsUnique();
        builder.HasIndex(x => x.StatusRequestId).IsUnique();
        builder.HasIndex(x => new { x.PaymentId, x.Number }).IsUnique();

        builder.HasOne<OutgoingPayment>()
            .WithMany()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
