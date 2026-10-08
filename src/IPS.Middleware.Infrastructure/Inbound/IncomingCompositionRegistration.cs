using IPS.Middleware.Application.Inbound.Composition;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Application.Inbound.StatusReports;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Inbound.Workers;
using IPS.Middleware.Infrastructure.Repositories.Inbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IPS.Middleware.Infrastructure.Inbound;

public static class IncomingCompositionRegistration
{
    // Callable composition only. The caller supplies persistence, protocol profile, protocol and remote ports.
    public static IServiceCollection AddIncomingComposition(this IServiceCollection services)
    {
        services.AddInboundFoundations();
        services.TryAddSingleton(new IncomingCompositionOptions());
        services.TryAddSingleton(new IncomingReplyOptions());
        services.TryAddSingleton(new IncomingReconciliationOptions());
        services.TryAddSingleton<IIncomingCoreReplyInterpreter, IncomingCoreReplyInterpreter>();
        services.TryAddSingleton<IIncomingTransferReplyInterpreter, IncomingCoreReplyInterpreter>();
        services.AddSingleton<InboundReplyChannel>();
        services.TryAddSingleton<IncomingTransportSettings>();
        services.TryAddSingleton(sp => new IncomingReplyAdmission(sp.GetRequiredService<IncomingTransportSettings>().Ips.ConnectionLimit - 1));
        services.AddSingleton<IIncomingWorkflowExecution, IncomingWorkflowExecution>();
        services.AddSingleton<IncomingComposition>();
        services.AddScoped<IIncomingCompositionRepository, IncomingCompositionRepository>();
        services.AddScoped<IncomingReceiptPreparation>();
        services.AddScoped<IncomingStatusReportProcessing>();
        services.AddScoped<IIncomingTransferRepository, IncomingTransferRepository>();
        services.AddScoped<IncomingRecallRefusals>();
        services.AddScoped<IncomingTransferRegistration>();
        services.AddScoped<IncomingTransferProcessing>();
        services.AddScoped<IncomingPacs008Processing>();
        services.AddScoped<IncomingReplyProcessing>();
        return services;
    }
}
