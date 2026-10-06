using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Execution;
using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Infrastructure.Payments.Execution;
using IPS.Middleware.Infrastructure.Payments.Investigation;
using IPS.Middleware.Infrastructure.Payments.Transport;

namespace IPS.Middleware.Api.Configuration;

internal static class OutgoingExecutionConfiguration
{
    internal static IServiceCollection AddOutgoingExecutionConfiguration(this IServiceCollection services)
    {
        services.AddSingleton(sp => sp.ReadSection<InvestigationOptions>("Payments:Outgoing:Investigation") ?? new InvestigationOptions());
        services.AddSingleton(sp => sp.ReadSection<OutgoingExecutionOptions>("Payments:Outgoing:Execution") ?? new OutgoingExecutionOptions());
        services.AddSingleton(ReadPaymentProfile);
        services.AddScoped<OutgoingTransactionIntake>();
        services.AddScoped<OutgoingTransactionWork>();
        services.AddScoped<Pacs008Processing>();
        services.AddScoped<OutgoingStatusDelivery>();
        services.AddOutgoingInvestigation();
        services.AddScoped(CreatePacs008Intake);
        services.AddSingleton<OutgoingRuntime>();
        services.AddSingleton<IOutgoingExecution>(sp => sp.GetRequiredService<OutgoingRuntime>());
        services.AddHostedService(sp => sp.GetRequiredService<OutgoingRuntime>());
        services.AddSingleton<OutgoingSubmission>();
        services.AddOptions<HostOptions>()
            .PostConfigure<OutgoingExecutionOptions, Pacs008Options, StatusDeliveryOptions, InvestigationOptions>(ExtendShutdownTimeout);
        return services;
    }

    internal static void ValidateOutgoingExecution(this IServiceProvider services)
    {
        _ = services.GetRequiredService<InvestigationOptions>();
        var execution = services.GetRequiredService<OutgoingExecutionOptions>();
        if (!execution.Enabled)
        {
            return;
        }

        var transport = services.GetRequiredService<OutgoingTransportSettings>();
        var payment = services.GetRequiredService<Pacs008Options>();
        var delivery = services.GetRequiredService<StatusDeliveryOptions>();

        var admissionFitsConnections = transport.Enabled
            && execution.Concurrency <= transport.Ips.ConnectionLimit
            && execution.CallbackConcurrency <= transport.Cbs.ConnectionLimit;
        if (!admissionFitsConnections)
        {
            throw new InvalidOperationException("Outgoing execution requires enabled transport and admission within each connection pool.");
        }

        var timeoutsAreOrdered = transport.Ips.RequestTimeout < execution.HttpWait
            && execution.AttemptBudget + payment.PersistenceBudget < payment.Ownership
            && transport.Cbs.RequestTimeout <= delivery.CallTimeout;
        if (!timeoutsAreOrdered)
        {
            throw new InvalidOperationException("Outgoing timeout ordering must fit the HTTP wait, attempt, callback and ownership budgets.");
        }

        _ = services.GetRequiredService<PaymentProfile>();
    }

    // Outgoing intake uses the outgoing policy and protocol profile; incoming workers register their own profile.
    private static Pacs008Intake CreatePacs008Intake(IServiceProvider services)
    {
        var profile = services.GetRequiredService<PaymentProfile>();
        return new Pacs008Intake(
            services.GetRequiredService<IOutgoingPaymentRepository>(),
            services.GetRequiredService<OutgoingTransactionIntake>(),
            profile.Policy,
            profile.Protocol,
            services.GetRequiredService<Pacs008Options>(),
            services.GetRequiredService<TimeProvider>());
    }

    private static PaymentProfile ReadPaymentProfile(IServiceProvider services)
    {
        var settings = services.ReadSection<PolicySettings>("Payments:Outgoing:Policy") ?? new PolicySettings();
        var participantBic = services.GetRequiredService<OutgoingTransportSettings>().ParticipantBic.ToUpperInvariant();
        var policy = new Pacs008Policy(participantBic, settings.TreasuryBic, settings.Currencies, settings.IndirectParticipants);
        var protocol = services.ReadSection<Pacs008ProtocolProfile>("Payments:Outgoing:Protocol")
            ?? throw new InvalidOperationException("Outgoing execution requires Payments:Outgoing:Protocol:IpsBic.");
        return new PaymentProfile(policy, protocol);
    }

    // Shutdown leaves room to drain admitted work and persist evidence of its last attempts.
    private static void ExtendShutdownTimeout(
        HostOptions host,
        OutgoingExecutionOptions execution,
        Pacs008Options payment,
        StatusDeliveryOptions delivery,
        InvestigationOptions investigation)
    {
        if (!execution.Enabled)
        {
            return;
        }

        host.ServicesStopConcurrently = true;
        TimeSpan[] persistenceBudgets = [payment.PersistenceBudget, delivery.PersistenceBudget, investigation.PersistenceBudget];
        var required = execution.ShutdownBudget + persistenceBudgets.Max();
        if (host.ShutdownTimeout < required)
        {
            host.ShutdownTimeout = required;
        }
    }

    private sealed record PaymentProfile(Pacs008Policy Policy, Pacs008ProtocolProfile Protocol);

    private sealed class PolicySettings
    {
        public string? TreasuryBic { get; set; }
        public PaymentCurrency[] Currencies { get; set; } = [];
        public string[] IndirectParticipants { get; set; } = [];
    }
}
