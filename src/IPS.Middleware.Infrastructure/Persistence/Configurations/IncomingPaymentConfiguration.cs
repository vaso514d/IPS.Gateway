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

    public void Configure(EntityTypeBuilder<IncomingPayment> payment)
    {
        payment.ToTable("IncomingPayments", table =>
        {
            table.HasCheckConstraint("CK_IncomingPayments_Request", "ISJSON([RequestJson]) = 1");
            table.HasCheckConstraint("CK_IncomingPayments_Claim",
                "([ClaimToken] IS NULL AND [ClaimExpiresAtUtc] IS NULL) OR ([ClaimToken] IS NOT NULL AND [ClaimExpiresAtUtc] IS NOT NULL)");
        });
        payment.HasKey(p => p.Id);
        payment.Property(p => p.Id).ValueGeneratedNever();
        payment.HasAggregateIdentity(IncomingPaymentKind);
        payment.Property(p => p.ParticipantBic).HasMaxLength(11).UseCollation(BinaryCollation);
        payment.Property(p => p.EndToEndId).HasMaxLength(35).UseCollation(BinaryCollation);
        // SQL equality ignores trailing spaces, even with a binary collation; the byte length keeps uniqueness ordinal.
        payment.Property<int>(EndToEndIdBytes).HasComputedColumnSql("DATALENGTH([EndToEndId])", stored: true);
        payment.HasIndex(nameof(IncomingPayment.ParticipantBic), nameof(IncomingPayment.EndToEndId), EndToEndIdBytes).IsUnique();
        payment.Property(p => p.EventSequence).HasColumnName("LastSequence");
        payment.Ignore(p => p.PendingEvents);
        payment.Ignore(p => p.CoreResult);
        payment.Ignore(p => p.IpsDecision);
        payment.Property<string>(IncomingPaymentColumns.ContextJson).IsRequired();
        payment.Property<long>(IncomingPaymentColumns.CheckpointVersion);
        payment.Property<DateTimeOffset?>(IncomingPaymentColumns.FollowUpAtUtc);
        payment.Property<DateTimeOffset?>(IncomingPaymentColumns.ReconciliationDeadlineUtc);
        payment.ToTable(table => table.HasCheckConstraint("CK_IncomingPayments_FollowUpDeadline",
            "[FollowUpAtUtc] IS NULL OR ([ReconciliationDeadlineUtc] IS NOT NULL AND [FollowUpAtUtc] <= [ReconciliationDeadlineUtc])"));
        payment.ToTable(table => table.HasCheckConstraint("CK_IncomingPayments_Context", "ISJSON([ContextJson]) = 1"));
        payment.Property<string>(RequestJson).IsRequired();
        payment.Property<Guid?>(ClaimToken);
        payment.Property<DateTimeOffset?>(ClaimExpiresAtUtc);
        payment.Property<DateTimeOffset?>(NextActionAtUtc);
        payment.Property<byte[]>(RowVersion).IsRequired().IsRowVersion();
        payment.HasIndex(IncomingPaymentColumns.FollowUpAtUtc, nameof(IncomingPayment.RegisteredAtUtc), nameof(IncomingPayment.Id));
        payment.HasIndex(NextActionAtUtc, nameof(IncomingPayment.RegisteredAtUtc), nameof(IncomingPayment.Id));
    }
}
