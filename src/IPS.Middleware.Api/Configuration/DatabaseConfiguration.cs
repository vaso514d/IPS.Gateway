using IPS.Middleware.Infrastructure.Persistence;

namespace IPS.Middleware.Api.Configuration;

internal static class DatabaseConfiguration
{
    internal const string MigrateOnStartupKey = "Database:MigrateOnStartup";

    // Off by default: the host then never creates or changes its database. When enabled, the database is migrated before
    // any request is served or worker starts, and a failed migration stops startup.
    internal static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue<bool>(MigrateOnStartupKey))
        {
            return;
        }

        services.RequireDatabase(MigrateOnStartupKey);
        await DatabaseMigration.ApplyAsync(configuration.GetConnectionString(ConfigurationReading.ConnectionName)!, cancellationToken);
    }
}
