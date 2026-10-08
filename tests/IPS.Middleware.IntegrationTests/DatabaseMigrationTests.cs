using System.Net;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IPS.Middleware.IntegrationTests;

public sealed class DatabaseMigrationTests
{
    private const string Prefix = "IPS_Middleware_Tests_";

    [Fact]
    public async Task Enabled_migration_creates_the_database_and_applies_every_migration_before_serving()
    {
        var connectionString = LocalDb(Prefix + Guid.NewGuid().ToString("N"));
        try
        {
            using (var factory = new MigrationFactory("true", connectionString))
            using (var client = factory.CreateClient())
            using (var response = await client.GetAsync("/health/live"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            await using var context = Context(connectionString);
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());

            // A second start finds nothing to apply and still starts.
            using var restarted = new MigrationFactory("true", connectionString);
            using var again = restarted.CreateClient();
            using var live = await again.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }
        finally
        {
            await using var context = Context(connectionString);
            await context.Database.EnsureDeletedAsync();
            SqlConnection.ClearAllPools();
        }
    }

    [Fact]
    public void Enabled_migration_without_a_connection_string_stops_startup()
    {
        using var factory = new MigrationFactory("true", "");
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("ConnectionStrings:Middleware", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disabled_migration_never_contacts_the_database()
    {
        // An address that cannot answer: starting would fail if the host tried to connect.
        using var factory = new MigrationFactory("false", "Server=tcp:127.0.0.1,1;Database=Unreachable;Connect Timeout=1;Trust Server Certificate=true");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string LocalDb(string database) => new SqlConnectionStringBuilder
    {
        DataSource = @"(localdb)\MSSQLLocalDB",
        InitialCatalog = database,
        IntegratedSecurity = true,
        TrustServerCertificate = true,
        ConnectTimeout = 30
    }.ConnectionString;

    private static TransactionDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<TransactionDbContext>().UseSqlServer(connectionString).Options);

    private sealed class MigrationFactory(string enabled, string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Database:MigrateOnStartup"] = enabled,
                    ["ConnectionStrings:Middleware"] = connectionString
                }));
        }
    }
}
