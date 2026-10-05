using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Execution;
using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Infrastructure.Payments.Execution;
using IPS.Middleware.Infrastructure.Payments.Transport;

namespace IPS.Middleware.Api.Configuration;

internal static class OutgoingExecutionConfiguration
{
    internal static IServiceCollection AddOutgoingExecutionConfiguration(this IServiceCollection services)
    {
        services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>().GetSection("Payments:Outgoing:Investigation")
            .Get<InvestigationOptions>(o => o.ErrorOnUnknownConfiguration = true) ?? new());
        services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>().GetSection("Payments:Outgoing:Execution")
            .Get<OutgoingExecutionOptions>(o => o.ErrorOnUnknownConfiguration = true) ?? new());
        services.AddSingleton(CreatePaymentProfile);
        services.AddScoped<OutgoingTransactionIntake>();
        services.AddScoped<OutgoingTransactionWork>();
        services.AddScoped<Pacs008Processing>();
        services.AddScoped<OutgoingStatusDelivery>();
        services.AddScoped(sp => new Pacs008Intake(sp.GetRequiredService<IOutgoingPaymentRepository>(),
            sp.GetRequiredService<OutgoingTransactionIntake>(), sp.GetRequiredService<PaymentProfile>().Policy,
            sp.GetRequiredService<PaymentProfile>().Protocol, sp.GetRequiredService<Pacs008Options>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<OutgoingRuntime>();
        services.AddSingleton<IOutgoingExecution>(sp => sp.GetRequiredService<OutgoingRuntime>());
        services.AddHostedService(sp => sp.GetRequiredService<OutgoingRuntime>());
        services.AddSingleton<OutgoingSubmission>();
        services.AddOptions<HostOptions>().PostConfigure<OutgoingExecutionOptions, Pacs008Options, StatusDeliveryOptions>((host, execution, payment, delivery) =>
        {
            if (execution.Enabled)
            {
                host.ServicesStopConcurrently = true;
                var required = execution.ShutdownBudget + (payment.PersistenceBudget > delivery.PersistenceBudget ? payment.PersistenceBudget : delivery.PersistenceBudget);
                if (host.ShutdownTimeout < required)
                {
                    host.ShutdownTimeout = required;
                }
            }
        });
        return services;
    }

    private static PaymentProfile CreatePaymentProfile(IServiceProvider sp)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var settings = config.GetSection("Payments:Outgoing:Policy").Get<PolicySettings>(o => o.ErrorOnUnknownConfiguration = true) ?? new();
        var policy = new Pacs008Policy(sp.GetRequiredService<OutgoingTransportSettings>().ParticipantBic.ToUpperInvariant(),
            settings.TreasuryBic, settings.Currencies, settings.IndirectParticipants);
        var profile = config.GetSection("Payments:Outgoing:Protocol").Get<Pacs008ProtocolProfile>(o => o.ErrorOnUnknownConfiguration = true)
            ?? throw new InvalidOperationException("Outgoing execution requires Payments:Outgoing:Protocol:IpsBic.");
        return new PaymentProfile(policy, profile);
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
        if (!transport.Enabled || execution.Concurrency > transport.Ips.ConnectionLimit || execution.CallbackConcurrency > transport.Cbs.ConnectionLimit)
        {
            throw new InvalidOperationException("Outgoing execution requires enabled transport and admission within each connection pool.");
        }

        if (transport.Ips.RequestTimeout >= execution.HttpWait || execution.AttemptBudget + payment.PersistenceBudget >= payment.Ownership ||
            transport.Cbs.RequestTimeout > delivery.CallTimeout)
        {
            throw new InvalidOperationException("Outgoing timeout ordering must fit the HTTP wait, attempt, callback and ownership budgets.");
        }

        _ = services.GetRequiredService<PaymentProfile>();
    }

    private sealed class PaymentProfile
    {
        public PaymentProfile(Pacs008Policy policy, Pacs008ProtocolProfile protocol)
        {
            Policy = policy;
            Protocol = protocol;
        }

        public Pacs008Policy Policy { get; init; }
        public Pacs008ProtocolProfile Protocol { get; init; }
    }

    private sealed class PolicySettings
    {
        public string? TreasuryBic { get; set; }
        public PaymentCurrency[] Currencies { get; set; } = [];
        public string[] IndirectParticipants { get; set; } = [];
    }
}
