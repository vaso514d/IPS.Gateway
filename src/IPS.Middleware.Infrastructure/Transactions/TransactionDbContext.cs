using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Transactions;

public sealed class TransactionDbContext(DbContextOptions<TransactionDbContext> options) : DbContext(options)
{
    internal DbSet<TransactionRow> Transactions => Set<TransactionRow>();
    internal DbSet<TransactionHistoryRow> History => Set<TransactionHistoryRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var transaction = modelBuilder.Entity<TransactionRow>();
        transaction.ToTable("Transactions");
        transaction.HasKey(row => row.Id);
        transaction.Property(row => row.Id).ValueGeneratedNever();
        transaction.Property(row => row.MessageType).HasMaxLength(16);
        transaction.Property(row => row.ClientReference).HasMaxLength(35);
        transaction.HasIndex(row => row.ClientReference).IsUnique();
        transaction.Property(row => row.RowVersion).IsRowVersion();
        transaction.HasMany(row => row.History).WithOne().HasForeignKey(row => row.TransactionId);

        var history = modelBuilder.Entity<TransactionHistoryRow>();
        history.ToTable("TransactionHistory");
        history.HasKey(row => new { row.TransactionId, row.Sequence });
        history.Property(row => row.ReasonCode).HasMaxLength(35);
        history.Property(row => row.Description).HasMaxLength(2000);
    }
}
