using IPS.Middleware.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static IPS.Middleware.Infrastructure.Persistence.Events.EventRegistry;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class OutgoingPaymentConfiguration : IEntityTypeConfiguration<OutgoingPayment>
{
    public void Configure(EntityTypeBuilder<OutgoingPayment> payment)
    {
        payment.ToTable("Transactions");
        payment.HasKey(p => p.Id);
        payment.Property(p => p.Id).ValueGeneratedNever();
        payment.HasAggregateIdentity(OutgoingPaymentKind);
        payment.Property(p => p.MessageType).HasMaxLength(16);
        payment.Property(p => p.ClientReference).HasMaxLength(35);
        payment.HasIndex(p => p.ClientReference).IsUnique();
        payment.Property(p => p.EventSequence).HasColumnName("LastSequence");
        payment.Property(p => p.CurrentReasonCode).HasMaxLength(35);
        payment.Property(p => p.CurrentDescription).HasMaxLength(2000);
        payment.Ignore(p => p.PendingEvents);
        payment.Ignore(p => p.Current);
        payment.Ignore(p => p.IsFinal);
        payment.Property<string>(RequestJson).IsRequired();
        payment.Property<string>(AcceptedJson);
        payment.ToTable(table => table.HasCheckConstraint("CK_Transactions_Accepted",
            "[AcceptedJson] IS NULL OR ([MessageType] = 'pacs.008' AND ISJSON([AcceptedJson]) = 1)"));
        payment.Property<string>(MessageId).HasMaxLength(35);
        payment.Property<string>(ProtocolTransactionId).HasMaxLength(35);
        payment.Property<string>(UnsignedXml);
        payment.Property<string>(SignedXml);
        payment.Property<string>(SubmissionJson);
        payment.Property<string>(SubmissionResponseJson);
        payment.ToTable(table => table.HasCheckConstraint("CK_Transactions_Submission",
            "([SubmissionJson] IS NULL AND [SubmissionResponseJson] IS NULL) OR " +
            "([SubmissionJson] IS NOT NULL AND ISJSON([SubmissionJson]) = 1 AND [MessageType] = 'pacs.008' " +
            "AND [MessageId] IS NOT NULL AND [ProtocolTransactionId] IS NOT NULL AND [UnsignedXml] IS NOT NULL " +
            "AND ([SubmissionResponseJson] IS NULL OR ISJSON([SubmissionResponseJson]) = 1))"));
        payment.HasIndex(MessageId).IsUnique().HasFilter("[MessageId] IS NOT NULL");
        payment.HasIndex(ProtocolTransactionId).IsUnique().HasFilter("[ProtocolTransactionId] IS NOT NULL");
        payment.ToTable(table => table.HasCheckConstraint("CK_Transactions_Preparation",
            "([MessageId] IS NULL AND [ProtocolTransactionId] IS NULL AND [UnsignedXml] IS NULL AND [SignedXml] IS NULL) OR " +
            "([MessageType] = 'pacs.008' AND [MessageId] IS NOT NULL AND [ProtocolTransactionId] IS NOT NULL " +
            "AND ([SignedXml] IS NULL OR [UnsignedXml] IS NOT NULL))"));
        payment.Property<TransactionDirection>(Direction);
        payment.Property<Guid?>(ClaimToken);
        payment.Property<DateTimeOffset?>(ClaimExpiresAtUtc);
        payment.Property<DateTimeOffset?>(NextActionAtUtc);
        payment.Property<byte[]>(RowVersion).IsRequired().IsRowVersion();
        payment.HasIndex(Direction, nameof(OutgoingPayment.CurrentStatus), nameof(OutgoingPayment.MessageType),
            nameof(OutgoingPayment.CurrentStatusAtUtc), nameof(OutgoingPayment.Id));
        payment.HasIndex(ClaimExpiresAtUtc);
        payment.ToTable(table => table.HasCheckConstraint("CK_Transactions_Claim",
            "([ClaimToken] IS NULL AND [ClaimExpiresAtUtc] IS NULL) OR ([ClaimToken] IS NOT NULL AND [ClaimExpiresAtUtc] IS NOT NULL)"));
    }
}
