using IPS.Middleware.Infrastructure.Inbound.Transport;
using Microsoft.Data.SqlClient;

namespace IPS.Middleware.Api.Configuration;

internal static class IncomingTransportConfiguration
{
    internal static IServiceCollection AddIncomingTransportConfiguration(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var settings = configuration.GetSection("Payments:Incoming:Transport").Get<IncomingTransportSettings>(
                options => options.ErrorOnUnknownConfiguration = true) ?? new();
            settings.Validate(sp.GetRequiredService<IHostEnvironment>().IsDevelopment());
            if (settings.Enabled)
            {
                var connection = new SqlConnectionStringBuilder(configuration.GetConnectionString("Middleware") ?? "");
                if (string.IsNullOrWhiteSpace(connection.DataSource) || string.IsNullOrWhiteSpace(connection.InitialCatalog))
                    throw new InvalidOperationException("Live incoming transport requires ConnectionStrings:Middleware with a server and database.");
            }
            return settings;
        });
        services.AddIncomingHttpClients();
        return services;
    }

    internal static void ValidateIncomingTransport(this IServiceProvider services)
    {
        if (services.GetRequiredService<IncomingTransportSettings>().Enabled)
        {
            _ = services.GetRequiredService<IncomingTransportCertificates>();
            services.ValidateIncomingHttpClients();
        }
    }
}
