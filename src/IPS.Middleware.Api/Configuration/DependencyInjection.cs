using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Api.Configuration;

internal static class DependencyInjection
{
    internal static IServiceCollection AddMiddleware(this IServiceCollection services)
    {
        return services
            .AddPaymentSettings()
            .AddIncomingTransportConfiguration()
            .AddOutgoingTransportConfiguration()
            .AddIncomingWorkerConfiguration()
            .AddOutgoingExecutionConfiguration()
            .AddOutgoingApi()
            .AddSigning();
    }

    // Runs once host configuration is final, so invalid settings stop startup before any request is served.
    internal static void ValidateMiddleware(this IServiceProvider services)
    {
        services.ValidatePaymentSettings();
        _ = services.GetRequiredService<Pacs008SigningPolicy>();
        services.ValidateIncomingTransport();
        services.ValidateOutgoingTransport();
        services.ValidateIncomingWorkers();
        services.ValidateOutgoingExecution();
    }

    private static IServiceCollection AddSigning(this IServiceCollection services)
    {
        services.AddSingleton(sp => new Pacs008SigningPolicy(
            sp.GetRequiredService<IConfiguration>().GetValue<bool>(Pacs008SigningPolicy.AllowUnsignedConfigurationKey),
            sp.GetRequiredService<IHostEnvironment>().IsDevelopment()));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<Pacs008MessageSigner>();
        return services;
    }
}
