using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Persistence;

public static class DatabaseMigration
{
    // Creates the database when it is missing and applies every pending migration. EF Core holds an application lock on
    // SQL Server while migrating, so instances starting together apply each migration once.
    public static async Task ApplyAsync(string connectionString, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TransactionDbContext>().UseSqlServer(connectionString).Options;
        await using var context = new TransactionDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);
    }
}
