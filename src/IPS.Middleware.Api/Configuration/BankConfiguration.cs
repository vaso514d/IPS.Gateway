using IPS.Middleware.Application.Payments;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Payments.Transport;
using IPS.Middleware.Infrastructure.Proxy;

namespace IPS.Middleware.Api.Configuration;

// One bank identity (decision 016) for every message, transport channel and participant check.
internal static class BankConfiguration
{
    internal static IServiceCollection AddBankConfiguration(this IServiceCollection services)
    {
        services.AddSingleton(ReadIdentity);
        return services;
    }

    // Only the live features use the codes, so a host with all of them disabled starts without a Bank section.
    internal static void ValidateBank(this IServiceProvider services)
    {
        var isUsed = services.GetRequiredService<OutgoingTransportSettings>().Enabled
            || services.GetRequiredService<IncomingTransportSettings>().Enabled
            || services.GetRequiredService<ProxySettings>().Enabled;
        if (isUsed)
        {
            _ = services.GetRequiredService<BankIdentity>();
        }
    }

    private static BankIdentity ReadIdentity(IServiceProvider services)
    {
        var settings = services.ReadSection<BankSettings>("Bank") ?? new BankSettings();
        try
        {
            return new BankIdentity(settings.BicCode, settings.NbgCode, settings.TreasuryCode);
        }
        catch (ArgumentException error)
        {
            throw new InvalidOperationException(
                "Bank requires BicCode and NbgCode as 8 or 11 character BICs, an optional TreasuryCode, and three different codes.", error);
        }
    }

    private sealed class BankSettings
    {
        public string BicCode { get; set; } = "";
        public string NbgCode { get; set; } = "";
        public string? TreasuryCode { get; set; }
    }
}
