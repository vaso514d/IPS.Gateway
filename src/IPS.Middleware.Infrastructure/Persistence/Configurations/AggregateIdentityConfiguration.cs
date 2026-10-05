using IPS.Middleware.Domain;
using IPS.Middleware.Infrastructure.Persistence.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static IPS.Middleware.Infrastructure.Persistence.Events.EventRegistry;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class AggregateIdentityConfiguration : IEntityTypeConfiguration<AggregateIdentity>
{
    public void Configure(EntityTypeBuilder<AggregateIdentity> builder)
    {
        builder.ToTable("AggregateIdentities", table => table.HasCheckConstraint(
            "CK_AggregateIdentities_Kind",
            $"[Kind] IN ('{OutgoingPaymentKind}', '{IncomingPaymentKind}')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasMaxLength(AggregateIdentity.KindLength).IsUnicode(false);
        builder.HasAlternateKey(x => new { x.Id, x.Kind });
    }
}

internal static class AggregateIdentityMapping
{
    // A typed foreign key: the state row's fixed kind must match its shared identity.
    internal static void HasAggregateIdentity<T>(this EntityTypeBuilder<T> builder, string kind) where T : AggregateRoot
    {
        builder.Property<string>(AggregateIdentity.KindColumn)
            .IsRequired()
            .HasMaxLength(AggregateIdentity.KindLength)
            .IsUnicode(false)
            .HasComputedColumnSql($"CONVERT(varchar({AggregateIdentity.KindLength}), '{kind}')", stored: true);

        builder.HasOne<AggregateIdentity>()
            .WithOne()
            .HasForeignKey<T>(nameof(AggregateRoot.Id), AggregateIdentity.KindColumn)
            .HasPrincipalKey<AggregateIdentity>(x => new { x.Id, x.Kind })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
