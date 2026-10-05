using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class InvestigationConfiguration : IEntityTypeConfiguration<InvestigationRow>
{
    public void Configure(EntityTypeBuilder<InvestigationRow> row)
    {
        row.ToTable("OutgoingInvestigations", table => table.HasCheckConstraint("CK_OutgoingInvestigations_State",
            "[Number] > 0 AND (([Outcome] IS NULL AND [CompletedAtUtc] IS NULL AND [DetailsJson] IS NULL AND [TransportFailure] IS NULL) OR " +
            "([Outcome] IS NOT NULL AND [Outcome] BETWEEN 0 AND 3 AND [CompletedAtUtc] IS NOT NULL AND [DetailsJson] IS NOT NULL AND ISJSON([DetailsJson]) = 1))"));
        row.HasKey(p => p.Id);
        row.Property(p => p.Id).ValueGeneratedNever();
        row.HasAlternateKey(p => new { p.Id, p.PaymentId });
        row.Property(p => p.MessageId).HasMaxLength(35).UseCollation(PaymentColumns.BinaryCollation);
        row.Property(p => p.StatusRequestId).HasMaxLength(35).UseCollation(PaymentColumns.BinaryCollation);
        row.HasIndex(p => p.MessageId).IsUnique();
        row.HasIndex(p => p.StatusRequestId).IsUnique();
        row.HasIndex(p => new { p.PaymentId, p.Number }).IsUnique();
        row.HasOne<OutgoingPayment>().WithMany().HasForeignKey(p => p.PaymentId).OnDelete(DeleteBehavior.NoAction);
    }
}
