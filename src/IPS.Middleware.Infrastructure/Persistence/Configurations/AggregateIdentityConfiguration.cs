using IPS.Middleware.Domain;
using IPS.Middleware.Infrastructure.Persistence.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static IPS.Middleware.Infrastructure.Persistence.Events.EventRegistry;

namespace IPS.Middleware.Infrastructure.Persistence.Configurations;

internal sealed class AggregateIdentityConfiguration : IEntityTypeConfiguration<AggregateIdentity>
{
    public void Configure(EntityTypeBuilder<AggregateIdentity> identity)
    {
        identity.ToTable("AggregateIdentities", table => table.HasCheckConstraint("CK_AggregateIdentities_Kind",
            $"[Kind] IN ('{OutgoingPaymentKind}', '{IncomingPaymentKind}')"));
        identity.HasKey(i => i.Id);
        identity.Property(i => i.Id).ValueGeneratedNever();
        identity.Property(i => i.Kind).HasMaxLength(AggregateIdentity.KindLength).IsUnicode(false);
        identity.HasAlternateKey(i => new { i.Id, i.Kind });
    }
}

internal static class AggregateIdentityMapping
{
    /// <summary>A typed foreign key: the state row's fixed kind must match its shared identity.</summary>
    internal static void HasAggregateIdentity<T>(this EntityTypeBuilder<T> state, string kind) where T : AggregateRoot
    {
        state.Property<string>(AggregateIdentity.KindColumn).IsRequired().HasMaxLength(AggregateIdentity.KindLength).IsUnicode(false)
            .HasComputedColumnSql($"CONVERT(varchar({AggregateIdentity.KindLength}), '{kind}')", stored: true);
        state.HasOne<AggregateIdentity>().WithOne()
            .HasForeignKey<T>(nameof(AggregateRoot.Id), AggregateIdentity.KindColumn)
            .HasPrincipalKey<AggregateIdentity>(i => new { i.Id, i.Kind })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
