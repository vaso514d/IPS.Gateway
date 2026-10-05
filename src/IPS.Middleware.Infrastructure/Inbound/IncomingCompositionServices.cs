using IPS.Middleware.Application.Inbound.Composition;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Repositories.Inbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IPS.Middleware.Infrastructure.Inbound;

public static class IncomingCompositionServices
{
    /// <summary>Callable composition only. The caller supplies persistence, protocol profile, protocol and remote ports.</summary>
    public static IServiceCollection AddIncomingComposition(this IServiceCollection services)
    {
        services.AddInboundFoundations();
        services.TryAddSingleton(new IncomingCompositionOptions());
        services.TryAddSingleton(new IncomingReplyOptions());
        services.TryAddSingleton(new IncomingReconciliationOptions());
        services.TryAddSingleton<IIncomingCoreReplyInterpreter, IncomingCoreReplyInterpreter>();
        services.AddSingleton<InboundReplyChannel>();
        services.AddSingleton<IIncomingWorkflowExecution, IncomingWorkflowExecution>();
        services.AddSingleton<IncomingComposition>();
        services.AddScoped<IIncomingCompositionRepository, IncomingCompositionRepository>();
        services.AddScoped<IncomingReceiptPreparation>();
        services.AddScoped<IncomingPacs008Processing>();
        services.AddScoped<IncomingReplyProcessing>();
        return services;
    }
}
