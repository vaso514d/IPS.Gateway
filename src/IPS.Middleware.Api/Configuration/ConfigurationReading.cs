using Microsoft.Data.SqlClient;

namespace IPS.Middleware.Api.Configuration;

internal static class ConfigurationReading
{
    internal const string ConnectionName = "Middleware";

    // Unknown keys fail startup instead of being silently ignored.
    internal static T? ReadSection<T>(this IServiceProvider services, string section) where T : class =>
        services.GetRequiredService<IConfiguration>()
            .GetSection(section)
            .Get<T>(options => options.ErrorOnUnknownConfiguration = true);

    internal static void RequireDatabase(this IServiceProvider services, string feature)
    {
        var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionName) ?? "";
        var connection = new SqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(connection.DataSource) || string.IsNullOrWhiteSpace(connection.InitialCatalog))
        {
            throw new InvalidOperationException($"{feature} requires ConnectionStrings:{ConnectionName} with a server and database.");
        }
    }
}
