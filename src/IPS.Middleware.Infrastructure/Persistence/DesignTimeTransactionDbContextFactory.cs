using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IPS.Middleware.Infrastructure.Transactions;

public sealed class DesignTimeTransactionDbContextFactory : IDesignTimeDbContextFactory<TransactionDbContext>
{
    public TransactionDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<TransactionDbContext>()
            .UseSqlServer()
            .Options);
}
