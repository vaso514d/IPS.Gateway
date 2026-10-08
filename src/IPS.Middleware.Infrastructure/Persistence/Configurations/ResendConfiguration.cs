using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class ResendConfiguration : IEntityTypeConfiguration<ResendRow>
{
    public void Configure(EntityTypeBuilder<ResendRow> builder)
    {
        builder.ToTable("OutgoingResends", table => table.HasCheckConstraint(
            "CK_OutgoingResends_State",
            "[Number] > 0 AND (([InvestigationId] IS NOT NULL AND [DeadlineUtc] IS NULL) OR ([InvestigationId] IS NULL AND [DeadlineUtc] IS NOT NULL)) AND (([Outcome] IS NULL AND [CompletedAtUtc] IS NULL AND [DetailsJson] IS NULL AND [TransportFailure] IS NULL) OR " +
            "([Outcome] IS NOT NULL AND [Outcome] BETWEEN 0 AND 2 AND [CompletedAtUtc] IS NOT NULL AND [DetailsJson] IS NOT NULL AND ISJSON([DetailsJson]) = 1))"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasAlternateKey(x => new { x.Id, x.PaymentId });
        builder.HasIndex(x => new { x.PaymentId, x.Number }).IsUnique();

        // One NotFound result authorizes at most one resend.
        builder.HasIndex(x => x.InvestigationId).IsUnique().HasFilter("[InvestigationId] IS NOT NULL");

        builder.HasOne<OutgoingPayment>()
            .WithMany()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<InvestigationRow>()
            .WithMany()
            .HasForeignKey(x => new { x.InvestigationId, x.PaymentId })
            .HasPrincipalKey(x => new { x.Id, x.PaymentId })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
