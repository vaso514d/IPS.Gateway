using IPS.Middleware.Api.Proxy;
using IPS.Middleware.Infrastructure.Proxy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace IPS.Middleware.Api.Configuration;

internal static class ProxyConfiguration
{
    internal static IServiceCollection AddProxyConfiguration(this IServiceCollection services)
    {
        services.AddSingleton(ReadSettings);
        services.AddProxyClient();
        services.AddOptions<MvcOptions>().Configure<ProxySettings>((mvc, settings) =>
            mvc.Conventions.Add(new ProxyControllerConvention(settings.Enabled)));
        return services;
    }

    internal static void ValidateProxy(this IServiceProvider services)
    {
        if (services.GetRequiredService<ProxySettings>().Enabled)
        {
            services.ValidateProxyClient();
        }
    }

    private static ProxySettings ReadSettings(IServiceProvider services)
    {
        var settings = services.ReadSection<ProxySettings>("Proxy") ?? new ProxySettings();
        settings.Validate(services.GetRequiredService<IHostEnvironment>().IsDevelopment());
        return settings;
    }

    // The routes exist only while proxy management is enabled.
    private sealed class ProxyControllerConvention(bool enabled) : IApplicationModelConvention
    {
        public void Apply(ApplicationModel application)
        {
            if (!enabled)
            {
                var proxy = application.Controllers.Single(controller => controller.ControllerType == typeof(ProxyController));
                application.Controllers.Remove(proxy);
            }
        }
    }
}
