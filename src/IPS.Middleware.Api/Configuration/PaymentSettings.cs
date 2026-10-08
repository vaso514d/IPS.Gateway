using IPS.Middleware.Application.Inbound.Composition;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Infrastructure.Inbound;

namespace IPS.Middleware.Api.Configuration;

internal static class PaymentSettings
{
    internal static IServiceCollection AddPaymentSettings(this IServiceCollection services)
    {
        services.AddSingleton(sp => sp.ReadSection<StatusDeliveryOptions>("Payments:Outgoing:StatusDelivery") ?? new StatusDeliveryOptions());
        services.AddSingleton(sp => sp.ReadSection<Pacs008Options>("Payments:Outgoing:Pacs008") ?? new Pacs008Options());
        services.AddSingleton(sp => sp.ReadSection<IncomingReplyOptions>("Payments:Incoming:Replies") ?? new IncomingReplyOptions());
        services.AddSingleton(sp => sp.ReadSection<IncomingProcessingOptions>("Payments:Incoming:Processing") ?? new IncomingProcessingOptions());
        services.AddSingleton(sp => (sp.ReadSection<ReconciliationSettings>("Payments:Incoming:Reconciliation") ?? new ReconciliationSettings()).ToOptions());
        services.AddSingleton(sp => sp.ReadSection<InboundSchedulingOptions>("Payments:Incoming:Scheduling") ?? new InboundSchedulingOptions());
        services.AddSingleton(sp => sp.ReadSection<IncomingCompositionOptions>("Payments:Incoming:Composition") ?? new IncomingCompositionOptions());
        return services;
    }

    // Options validate in their constructors, so resolving them rejects invalid budgets before requests are served.
    internal static void ValidatePaymentSettings(this IServiceProvider services)
    {
        _ = services.GetRequiredService<IncomingCompositionOptions>();
        _ = services.GetRequiredService<Pacs008Options>();
        _ = services.GetRequiredService<StatusDeliveryOptions>();
        _ = services.GetRequiredService<IncomingProcessingOptions>();
        _ = services.GetRequiredService<IncomingReplyOptions>();
        _ = services.GetRequiredService<IncomingReconciliationOptions>();
        _ = services.GetRequiredService<InboundSchedulingOptions>();
    }

    // Bind arrays through properties: constructor binding cannot reliably represent an empty JSON array.
    private sealed class ReconciliationSettings
    {
        public TimeSpan? CallTimeout { get; set; }
        public TimeSpan? PersistenceBudget { get; set; }
        public TimeSpan? Ownership { get; set; }
        public TimeSpan? Window { get; set; }
        public int DiscoveryBatch { get; set; } = 50;
        public TimeSpan[]? RetryDelays { get; set; }
        public TimeSpan? RepeatInterval { get; set; }

        public IncomingReconciliationOptions ToOptions() =>
            new(CallTimeout, PersistenceBudget, Ownership, Window, DiscoveryBatch, RetryDelays, RepeatInterval);
    }
}
