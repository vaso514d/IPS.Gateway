using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace IPS.Middleware.IntegrationTests.Transactions;

internal sealed class SqlTestDatabase : IAsyncDisposable
{
    private const string Prefix = "IPS_Middleware_Tests_";
    private readonly string _databaseName = Prefix + Guid.NewGuid().ToString("N");
    private readonly string _connectionString;

    private SqlTestDatabase()
    {
        _connectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = _databaseName,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 30
        }.ConnectionString;
        Factory = CreateFactory();
    }

    public IDbContextFactory<TransactionDbContext> Factory { get; }
    public SqlTransactionStore Store => new(Factory);

    public IDbContextFactory<TransactionDbContext> CreateFactory(params IInterceptor[] interceptors) =>
        new PooledDbContextFactory<TransactionDbContext>(new DbContextOptionsBuilder<TransactionDbContext>()
            .UseSqlServer(_connectionString).AddInterceptors(interceptors).Options);

    public static async Task<SqlTestDatabase> CreateAsync()
    {
        var database = new SqlTestDatabase();
        try
        {
            await using var db = await database.Factory.CreateDbContextAsync();
            await db.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var connection = new SqlConnectionStringBuilder(_connectionString);
        if (connection.DataSource != @"(localdb)\MSSQLLocalDB" ||
            connection.InitialCatalog != _databaseName || !_databaseName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to delete a database outside this test fixture.");
        }

        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.EnsureDeletedAsync();
        using var poolConnection = new SqlConnection(_connectionString);
        SqlConnection.ClearPool(poolConnection);
    }
}
