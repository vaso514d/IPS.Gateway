using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Events;
using IPS.Middleware.Infrastructure.Persistence.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

// The columns, keys and indexes every message table shares (specification 020).
internal static class MessageTableMapping
{
    internal const int BicLength = 11;
    internal const int AccountLength = 34;
    internal const int NameLength = 140;
    internal const int TextLength = 256;
    internal const int CurrencyLength = 3;

    // A row is linked to its payment aggregate; pacs.002 rows are keyed by their inbound journal entry instead.
    internal static void MapMessage<T>(this EntityTypeBuilder<T> builder, string table, bool linkedToPayment = true) where T : MessageRow
    {
        // The shared columns come first in every table; the message's own columns follow.
        builder.ToTable(table);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever().HasColumnOrder(0);
        builder.Property(x => x.Direction).HasColumnOrder(1);
        builder.Property(x => x.Status).HasColumnOrder(2);
        builder.Property(x => x.StatusAtUtc).HasColumnOrder(3);
        builder.Property(x => x.ReasonCode).HasMaxLength(MessageRow.ReferenceLength).HasColumnOrder(4);
        builder.Property(x => x.Description).HasMaxLength(MessageRow.DescriptionLength).HasColumnOrder(5);
        builder.Property(x => x.ClientReference).HasMaxLength(OutgoingPayment.ClientReferenceMaxLength).HasColumnOrder(6);
        builder.Property(x => x.CoreReference).HasMaxLength(TextLength).HasColumnOrder(7);
        builder.Property(x => x.MessageId).HasMaxLength(MessageRow.ReferenceLength).HasColumnOrder(8);
        builder.Property(x => x.CreatedAtUtc).HasColumnOrder(9);
        builder.HasIndex(x => new { x.Direction, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.Status, x.StatusAtUtc });
        builder.HasIndex(x => x.ClientReference).IsUnique().HasFilter("[ClientReference] IS NOT NULL");
        if (!linkedToPayment)
        {
            return;
        }

        builder.HasOne<AggregateIdentity>()
            .WithOne()
            .HasForeignKey<T>(x => x.Id)
            .OnDelete(DeleteBehavior.Restrict);
    }

    internal static PropertyBuilder<string?> Reference<T>(this EntityTypeBuilder<T> builder, System.Linq.Expressions.Expression<Func<T, string?>> property)
        where T : class => builder.Property(property).HasMaxLength(MessageRow.ReferenceLength);

    internal static PropertyBuilder<string?> Bic<T>(this EntityTypeBuilder<T> builder, System.Linq.Expressions.Expression<Func<T, string?>> property)
        where T : class => builder.Property(property).HasMaxLength(BicLength).IsUnicode(false);

    internal static PropertyBuilder<string?> Account<T>(this EntityTypeBuilder<T> builder, System.Linq.Expressions.Expression<Func<T, string?>> property)
        where T : class => builder.Property(property).HasMaxLength(AccountLength);

    internal static PropertyBuilder<string?> Name<T>(this EntityTypeBuilder<T> builder, System.Linq.Expressions.Expression<Func<T, string?>> property)
        where T : class => builder.Property(property).HasMaxLength(NameLength);

    internal static PropertyBuilder<string?> Text<T>(this EntityTypeBuilder<T> builder, System.Linq.Expressions.Expression<Func<T, string?>> property)
        where T : class => builder.Property(property).HasMaxLength(TextLength);

    internal static PropertyBuilder<string?> Currency<T>(this EntityTypeBuilder<T> builder, System.Linq.Expressions.Expression<Func<T, string?>> property)
        where T : class => builder.Property(property).HasMaxLength(CurrencyLength).IsUnicode(false);

    internal static PropertyBuilder<decimal?> Amount<T>(this EntityTypeBuilder<T> builder, System.Linq.Expressions.Expression<Func<T, decimal?>> property)
        where T : class => builder.Property(property).HasPrecision(18, 5);
}

internal sealed class Pacs008PaymentConfiguration : IEntityTypeConfiguration<Pacs008PaymentRow>
{
    public void Configure(EntityTypeBuilder<Pacs008PaymentRow> builder)
    {
        builder.MapMessage("Pacs008Payments");
        builder.Reference(x => x.InstructionId);
        builder.Reference(x => x.EndToEndId);
        builder.Reference(x => x.TransactionId);
        builder.Amount(x => x.Amount);
        builder.Currency(x => x.Currency);
        builder.Name(x => x.DebtorName);
        builder.Account(x => x.DebtorAccount);
        builder.Text(x => x.DebtorIdentification);
        builder.Bic(x => x.DebtorAgentBic);
        builder.Name(x => x.CreditorName);
        builder.Account(x => x.CreditorAccount);
        builder.Text(x => x.CreditorIdentification);
        builder.Bic(x => x.CreditorAgentBic);
        builder.Reference(x => x.CategoryPurpose);
        builder.Property(x => x.RemittanceInformation).HasMaxLength(MessageRow.DescriptionLength);
        builder.HasIndex(x => x.EndToEndId);
    }
}

internal sealed class Pacs009PaymentConfiguration : IEntityTypeConfiguration<Pacs009PaymentRow>
{
    public void Configure(EntityTypeBuilder<Pacs009PaymentRow> builder)
    {
        builder.MapMessage("Pacs009Payments");
        builder.Reference(x => x.InstructionId);
        builder.Reference(x => x.EndToEndId);
        builder.Reference(x => x.TransactionId);
        builder.Amount(x => x.Amount);
        builder.Currency(x => x.Currency);
        builder.Bic(x => x.DebtorAgentBic);
        builder.Account(x => x.DebtorAccount);
        builder.Bic(x => x.CreditorAgentBic);
        builder.Account(x => x.CreditorAccount);
        builder.Reference(x => x.CategoryPurpose);
        builder.Reference(x => x.Purpose);
        builder.HasIndex(x => x.EndToEndId);
    }
}

internal sealed class Pacs004PaymentConfiguration : IEntityTypeConfiguration<Pacs004PaymentRow>
{
    public void Configure(EntityTypeBuilder<Pacs004PaymentRow> builder)
    {
        builder.MapMessage("Pacs004Payments");
        builder.Reference(x => x.ReturnId);
        builder.Reference(x => x.ReturnReasonCode);
        builder.Amount(x => x.Amount);
        builder.Currency(x => x.Currency);
        builder.Bic(x => x.InstructedAgentBic);
        builder.Name(x => x.DebtorName);
        builder.Account(x => x.DebtorAccount);
        builder.Name(x => x.CreditorName);
        builder.Account(x => x.CreditorAccount);
        builder.Reference(x => x.OriginalMessageId);
        builder.Reference(x => x.OriginalMessageNameId);
        builder.Reference(x => x.OriginalInstructionId);
        builder.Reference(x => x.OriginalEndToEndId);
        builder.Reference(x => x.OriginalTransactionId);
        builder.Text(x => x.AdditionalInformation);
        builder.HasIndex(x => x.OriginalEndToEndId);
    }
}

internal sealed class Pain001InitiationConfiguration : IEntityTypeConfiguration<Pain001InitiationRow>
{
    public void Configure(EntityTypeBuilder<Pain001InitiationRow> builder)
    {
        builder.MapMessage("Pain001Initiations");
        builder.Reference(x => x.PaymentInformationId);
        builder.Reference(x => x.InstructionId);
        builder.Reference(x => x.EndToEndId);
        builder.Amount(x => x.Amount);
        builder.Currency(x => x.Currency);
        builder.Bic(x => x.InitiatingPartyBic);
        builder.Name(x => x.DebtorName);
        builder.Account(x => x.DebtorAccount);
        builder.Bic(x => x.DebtorAgentBic);
        builder.Name(x => x.CreditorName);
        builder.Account(x => x.CreditorAccount);
        builder.Bic(x => x.CreditorAgentBic);
        builder.Reference(x => x.CategoryPurpose);
        builder.Reference(x => x.Purpose);
        builder.Property(x => x.RemittanceInformation).HasMaxLength(MessageRow.DescriptionLength);
        builder.HasIndex(x => x.PaymentInformationId);
    }
}

internal sealed class Camt056RecallConfiguration : IEntityTypeConfiguration<Camt056RecallRow>
{
    public void Configure(EntityTypeBuilder<Camt056RecallRow> builder)
    {
        builder.MapMessage("Camt056Recalls");
        builder.Reference(x => x.RecallId);
        builder.Reference(x => x.OriginalMessageId);
        builder.Reference(x => x.OriginalEndToEndId);
        builder.Reference(x => x.OriginalTransactionId);
        builder.Amount(x => x.OriginalAmount);
        builder.Currency(x => x.OriginalCurrency);
        builder.Reference(x => x.RecallReasonCode);
        builder.Name(x => x.DebtorName);
        builder.Account(x => x.DebtorAccount);
        builder.Bic(x => x.DebtorAgentBic);
        builder.Name(x => x.CreditorName);
        builder.Account(x => x.CreditorAccount);
        builder.Bic(x => x.CreditorAgentBic);
        builder.HasIndex(x => x.OriginalEndToEndId);
    }
}

internal sealed class Camt055CancellationConfiguration : IEntityTypeConfiguration<Camt055CancellationRow>
{
    public void Configure(EntityTypeBuilder<Camt055CancellationRow> builder)
    {
        builder.MapMessage("Camt055Cancellations");
        builder.Reference(x => x.CancellationId);
        builder.Reference(x => x.PaymentCancellationId);
        builder.Bic(x => x.AssignerBic);
        builder.Bic(x => x.AssigneeBic);
        builder.Reference(x => x.OriginalMessageId);
        builder.Reference(x => x.OriginalPaymentInformationId);
        builder.Reference(x => x.OriginalInstructionId);
        builder.Reference(x => x.OriginalEndToEndId);
        builder.Amount(x => x.Amount);
        builder.Currency(x => x.Currency);
        builder.Reference(x => x.CancellationReasonCode);
        builder.Bic(x => x.OriginatorBic);
        builder.Name(x => x.CreditorName);
        builder.Account(x => x.CreditorAccount);
        builder.HasIndex(x => x.OriginalPaymentInformationId);
    }
}

internal sealed class Camt029ResolutionConfiguration : IEntityTypeConfiguration<Camt029ResolutionRow>
{
    public void Configure(EntityTypeBuilder<Camt029ResolutionRow> builder)
    {
        builder.MapMessage("Camt029Resolutions");
        builder.Reference(x => x.CancellationStatusId);
        builder.Reference(x => x.OriginalMessageId);
        builder.Reference(x => x.OriginalEndToEndId);
        builder.Reference(x => x.OriginalTransactionId);
        builder.Reference(x => x.ResolutionReasonCode);
        builder.Text(x => x.AdditionalInformation);
        builder.Amount(x => x.Amount);
        builder.Currency(x => x.Currency);
        builder.HasIndex(x => x.OriginalEndToEndId);
    }
}

internal sealed class Pain002StatusReportConfiguration : IEntityTypeConfiguration<Pain002StatusReportRow>
{
    public void Configure(EntityTypeBuilder<Pain002StatusReportRow> builder)
    {
        builder.MapMessage("Pain002StatusReports");
        builder.Reference(x => x.OriginalMessageId);
        builder.Reference(x => x.OriginalPaymentInformationId);
        builder.Reference(x => x.ReportReasonCode);
        builder.Text(x => x.AdditionalInformation);
        builder.Name(x => x.OriginatorName);
        builder.HasIndex(x => x.OriginalPaymentInformationId);
    }
}

internal sealed class Pacs002StatusReportConfiguration : IEntityTypeConfiguration<Pacs002StatusReportRow>
{
    public void Configure(EntityTypeBuilder<Pacs002StatusReportRow> builder)
    {
        builder.MapMessage("Pacs002StatusReports", linkedToPayment: false);
        builder.Reference(x => x.OriginalMessageId);
        builder.Reference(x => x.OriginalEndToEndId);
        builder.Reference(x => x.OriginalTransactionId);
        builder.Reference(x => x.TransactionStatusCode);
        builder.Reference(x => x.ReportReasonCode);
        builder.Text(x => x.AdditionalInformation);
        builder.HasIndex(x => x.OriginalEndToEndId);
    }
}
