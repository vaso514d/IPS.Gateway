using IPS.Middleware.Infrastructure.Payments.Transport;
using Microsoft.Data.SqlClient;

namespace IPS.Middleware.Api.Configuration;

internal static class OutgoingTransportConfiguration
{
    internal static IServiceCollection AddOutgoingTransportConfiguration(this IServiceCollection services)
    {
        services.AddSingleton(ReadSettings);
        services.AddOutgoingHttpClients();
        return services;
    }

    private static OutgoingTransportSettings ReadSettings(IServiceProvider sp)
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        var settings = configuration.GetSection("Payments:Outgoing:Transport").Get<OutgoingTransportSettings>(
            options => options.ErrorOnUnknownConfiguration = true) ?? new();
        settings.Validate(sp.GetRequiredService<IHostEnvironment>().IsDevelopment());
        if (settings.Enabled)
        {
            var connection = new SqlConnectionStringBuilder(configuration.GetConnectionString("Middleware") ?? "");
            if (string.IsNullOrWhiteSpace(connection.DataSource) || string.IsNullOrWhiteSpace(connection.InitialCatalog))
            {
                throw new InvalidOperationException("Live outgoing transport requires ConnectionStrings:Middleware with a server and database.");
            }
        }
        return settings;
    }

    internal static void ValidateOutgoingTransport(this IServiceProvider services)
    {
        if (services.GetRequiredService<OutgoingTransportSettings>().Enabled)
        {
            services.ValidateOutgoingHttpClients();
        }
    }
}
