using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class IncomingCoreCallConfiguration : IEntityTypeConfiguration<IncomingCoreCallRow>
{
    public void Configure(EntityTypeBuilder<IncomingCoreCallRow> builder)
    {
        builder.ToTable("IncomingCoreCalls", table =>
        {
            table.HasCheckConstraint("CK_IncomingCoreCalls_Kind", "[Kind] IN (0, 1, 2, 3)");
            table.HasCheckConstraint("CK_IncomingCoreCalls_Request",
                "([Kind] = 3 AND [RequestJson] IS NOT NULL AND ISJSON([RequestJson]) = 1) OR ([Kind] <> 3 AND [RequestJson] IS NULL)");
            table.HasCheckConstraint("CK_IncomingCoreCalls_Result",
                "([CompletionJson] IS NULL AND [Consumed] = 0) OR ([CompletionJson] IS NOT NULL AND ISJSON([CompletionJson]) = 1)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => new { x.PaymentId, x.Number }).IsUnique();
        builder.HasIndex(x => x.PaymentId).IsUnique().HasFilter("[Kind] = 0");
        builder.HasIndex(x => new { x.PaymentId, x.Kind }).IsUnique().HasFilter("[Kind] = 3");

        builder.HasOne<IncomingPayment>()
            .WithMany()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
