using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static IPS.Middleware.Infrastructure.Persistence.Events.EventRegistry;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class IncomingTransferConfiguration : IEntityTypeConfiguration<IncomingTransfer>
{
    internal const string KeyBytes = nameof(KeyBytes);

    public void Configure(EntityTypeBuilder<IncomingTransfer> builder)
    {
        builder.ToTable("IncomingTransfers", table =>
        {
            table.HasCheckConstraint("CK_IncomingTransfers_Request", "ISJSON([RequestJson]) = 1");
            table.HasCheckConstraint("CK_IncomingTransfers_Claim",
                "([ClaimToken] IS NULL AND [ClaimExpiresAtUtc] IS NULL) OR ([ClaimToken] IS NOT NULL AND [ClaimExpiresAtUtc] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasAggregateIdentity(IncomingTransferKind);
        builder.Property(x => x.ParticipantBic).HasMaxLength(11).UseCollation(BinaryCollation);
        builder.Property(x => x.Kind).HasMaxLength(16).IsUnicode(false).UseCollation(BinaryCollation);
        builder.Property(x => x.Key).HasMaxLength(35).UseCollation(BinaryCollation);
        // SQL equality ignores trailing spaces, even with a binary collation; the byte length keeps uniqueness ordinal.
        builder.Property<int>(KeyBytes).HasComputedColumnSql("DATALENGTH([Key])", stored: true);
        builder.HasIndex(nameof(IncomingTransfer.ParticipantBic), nameof(IncomingTransfer.Kind), nameof(IncomingTransfer.Key), KeyBytes).IsUnique();
        builder.Property(x => x.CoreReference).HasMaxLength(100);
        builder.Property(x => x.CoreReasonCode).HasMaxLength(35);
        builder.Property(x => x.CoreDescription).HasMaxLength(1000);
        builder.Property(x => x.ManualReviewReason).HasMaxLength(1000);
        builder.Property(x => x.EventSequence).HasColumnName("LastSequence");
        builder.Property<byte[]>(RowVersion).IsRequired().IsRowVersion();
        builder.Ignore(x => x.PendingEvents);
        builder.Ignore(x => x.IsFinal);
    }
}

// Technical state shares the transfer's row, so ownership and scheduling writes are fenced by the same row version.
internal sealed class IncomingTransferMetadataConfiguration : IEntityTypeConfiguration<IncomingTransferMetadata>
{
    public void Configure(EntityTypeBuilder<IncomingTransferMetadata> builder)
    {
        builder.ToTable("IncomingTransfers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RequestJson).IsRequired();
        builder.Property(x => x.RowVersion).IsRequired().IsRowVersion();
        builder.HasIndex(x => new { x.NextActionAtUtc, x.Id });

        builder.HasOne(x => x.Transfer)
            .WithOne()
            .HasForeignKey<IncomingTransfer>(x => x.Id)
            .IsRequired();
        builder.Navigation(x => x.Transfer).IsRequired();
    }
}
