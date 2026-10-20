using IPS.Middleware.Api.Authentication;
using IPS.MiidleWear.Security;
using Microsoft.AspNetCore.Authentication;

namespace IPS.Middleware.Api.Configuration;

// The verifier exists only when trusted certificates are configured; without them the marked endpoints are open (021).
internal sealed record CoreRequestSigning(IpsSignatureVerifier? Verifier, int TrustedCertificates);

internal static class CoreAuthenticationConfiguration
{
    internal const string Section = "CoreAuthentication";

    internal static IServiceCollection AddCoreAuthentication(this IServiceCollection services)
    {
        services.AddSingleton(ReadSigning);
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, CoreSignatureAuthentication>(CoreSignatureAuthentication.SchemeName, configureOptions: null);
        services.AddAuthorization();
        return services;
    }

    internal static void ValidateCoreAuthentication(this IServiceProvider services) =>
        _ = services.GetRequiredService<CoreRequestSigning>();

    private static CoreRequestSigning ReadSigning(IServiceProvider services)
    {
        var settings = services.ReadSection<CoreAuthenticationSettings>(Section) ?? new CoreAuthenticationSettings();
        var algorithms = settings.AllowedAlgorithms();
        if (!settings.RequiresSignature)
        {
            return new CoreRequestSigning(null, 0);
        }

        var timeProvider = services.GetRequiredService<TimeProvider>();
        var now = timeProvider.GetUtcNow();
        var certificates = settings.TrustedCertificates
            .Select(certificate => certificate.Load(privateKeyRequired: false, now))
            .ToArray();
        return new CoreRequestSigning(new IpsSignatureVerifier(certificates, algorithms, timeProvider), certificates.Length);
    }
}
