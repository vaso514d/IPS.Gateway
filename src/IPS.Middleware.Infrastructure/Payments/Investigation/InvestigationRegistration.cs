using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Payments.Investigation;

public static class InvestigationRegistration
{
    /// <summary>Callable workflow only. The caller supplies protocol trust/certificates and transport; no worker is started.</summary>
    public static IServiceCollection AddOutgoingInvestigation(this IServiceCollection services, InvestigationOptions options)
    {
        services.AddSingleton(options);
        services.AddScoped<IInvestigationRepository, InvestigationRepository>();
        services.AddScoped<OutgoingTransactionWork>();
        services.AddScoped<OutgoingInvestigation>();
        services.AddSingleton<InvestigationExecution>();
        return services;
    }
}
