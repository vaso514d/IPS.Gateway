using IPS.Middleware.Application.Payments.Investigation;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Payments.Investigation;

public static class InvestigationRegistration
{
    // The caller supplies options, the investigation protocol and the IPS transport.
    public static IServiceCollection AddOutgoingInvestigation(this IServiceCollection services)
    {
        services.AddScoped<OutgoingInvestigation>();
        services.AddScoped<OutgoingResend>();
        return services;
    }
}
