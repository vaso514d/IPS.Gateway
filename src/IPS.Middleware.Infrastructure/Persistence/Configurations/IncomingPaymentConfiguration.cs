using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static IPS.Middleware.Infrastructure.Persistence.Events.EventRegistry;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class IncomingPaymentConfiguration : IEntityTypeConfiguration<IncomingPayment>
{
    internal const string EndToEndIdBytes = nameof(EndToEndIdBytes);

    public void Configure(EntityTypeBuilder<IncomingPayment> builder)
    {
        builder.ToTable("IncomingPayments", table =>
        {
            table.HasCheckConstraint("CK_IncomingPayments_Request", "ISJSON([RequestJson]) = 1");
            table.HasCheckConstraint("CK_IncomingPayments_Claim",
                "([ClaimToken] IS NULL AND [ClaimExpiresAtUtc] IS NULL) OR ([ClaimToken] IS NOT NULL AND [ClaimExpiresAtUtc] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasAggregateIdentity(IncomingPaymentKind);
        builder.Property(x => x.ParticipantBic).HasMaxLength(11).UseCollation(BinaryCollation);
        builder.Property(x => x.EndToEndId).HasMaxLength(35).UseCollation(BinaryCollation);
        // SQL equality ignores trailing spaces, even with a binary collation; the byte length keeps uniqueness ordinal.
        builder.Property<int>(EndToEndIdBytes).HasComputedColumnSql("DATALENGTH([EndToEndId])", stored: true);
        builder.HasIndex(nameof(IncomingPayment.ParticipantBic), nameof(IncomingPayment.EndToEndId), EndToEndIdBytes).IsUnique();
        builder.Property(x => x.EventSequence).HasColumnName("LastSequence");
        builder.Property<byte[]>(RowVersion).IsRequired().IsRowVersion();
        builder.Ignore(x => x.PendingEvents);
        builder.Ignore(x => x.CoreResult);
        builder.Ignore(x => x.IpsDecision);
    }
}

// Technical state shares the payment's row, so ownership and processing writes are fenced by the same row version.
internal sealed class IncomingPaymentMetadataConfiguration : IEntityTypeConfiguration<IncomingPaymentMetadata>
{
    public void Configure(EntityTypeBuilder<IncomingPaymentMetadata> builder)
    {
        builder.ToTable("IncomingPayments", table =>
        {
            table.HasCheckConstraint("CK_IncomingPayments_FollowUpDeadline",
                "[FollowUpAtUtc] IS NULL OR ([ReconciliationDeadlineUtc] IS NOT NULL AND [FollowUpAtUtc] <= [ReconciliationDeadlineUtc])");
            table.HasCheckConstraint("CK_IncomingPayments_Context", "ISJSON([ContextJson]) = 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RequestJson).IsRequired();
        builder.Property(x => x.ContextJson).IsRequired();
        builder.Property(x => x.RowVersion).IsRequired().IsRowVersion();
        builder.HasIndex(x => new { x.FollowUpAtUtc, x.Id });
        builder.HasIndex(x => new { x.NextActionAtUtc, x.Id });

        builder.HasOne(x => x.Payment)
            .WithOne()
            .HasForeignKey<IncomingPayment>(x => x.Id)
            .IsRequired();
        builder.Navigation(x => x.Payment).IsRequired();
    }
}
