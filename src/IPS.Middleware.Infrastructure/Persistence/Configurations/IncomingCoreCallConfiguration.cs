using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class IncomingCoreCallConfiguration : IEntityTypeConfiguration<IncomingCoreCallRow>
{
    public void Configure(EntityTypeBuilder<IncomingCoreCallRow> call)
    {
        call.ToTable("IncomingCoreCalls", table =>
        {
            table.HasCheckConstraint("CK_IncomingCoreCalls_Kind", "[Kind] IN (0, 1, 2, 3)");
            table.HasCheckConstraint("CK_IncomingCoreCalls_Request", "([Kind] = 3 AND [RequestJson] IS NOT NULL AND ISJSON([RequestJson]) = 1) OR ([Kind] <> 3 AND [RequestJson] IS NULL)");
            table.HasCheckConstraint("CK_IncomingCoreCalls_Result", "([CompletionJson] IS NULL AND [Consumed] = 0) OR ([CompletionJson] IS NOT NULL AND ISJSON([CompletionJson]) = 1)");
        });
        call.HasKey(c => c.Id);
        call.Property(c => c.Id).ValueGeneratedNever();
        call.HasIndex(c => new { c.PaymentId, c.Number }).IsUnique();
        call.HasIndex(c => c.PaymentId).IsUnique().HasFilter("[Kind] = 0");
        call.HasIndex(c => new { c.PaymentId, c.Kind }).IsUnique().HasFilter("[Kind] = 3");
        call.HasOne<IncomingPayment>().WithMany().HasForeignKey(c => c.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}
