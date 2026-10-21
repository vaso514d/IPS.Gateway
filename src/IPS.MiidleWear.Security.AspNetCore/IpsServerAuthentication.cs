using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IPS.MiidleWear.Security.AspNetCore;

/// <summary>
/// Marks a controller or action that requires what the "Server" section configures: a listed API token, a signature by a
/// trusted certificate, or both. Endpoints that are not marked, such as health checks, stay open.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class RequireIpsAuthenticationAttribute : AuthorizeAttribute
{
    /// <summary>Requires the <see cref="IpsServerAuthentication.Scheme"/> scheme.</summary>
    public RequireIpsAuthenticationAttribute()
    {
        AuthenticationSchemes = IpsServerAuthentication.Scheme;
    }
}

/// <summary>What the "Server" section requires, loaded once: the accepted tokens and the signature verifier.</summary>
public sealed class IpsServerOptions : AuthenticationSchemeOptions
{
    /// <summary>The accepted tokens; none means no token is required.</summary>
    public IReadOnlyList<string> ApiTokens { get; set; } = [];

    /// <summary>The signature check; <c>null</c> means no signature is required.</summary>
    public IpsSignatureVerifier? Verifier { get; set; }

    /// <summary>The number of trusted certificates, for the startup log.</summary>
    public int TrustedCertificates { get; set; }
}

/// <summary>Registration of the "Server" section and its authentication.</summary>
public static class IpsServerAuthentication
{
    /// <summary>The authentication scheme name.</summary>
    public const string Scheme = "IpsServer";

    /// <summary>
    /// Applies <c>Server:Url</c> when it is set and registers the <see cref="Scheme"/> authentication with the tokens and
    /// certificates of the "Server" section, read when first used and checked when the host starts. Mark the endpoints
    /// with <see cref="RequireIpsAuthenticationAttribute"/> or <see cref="RequireIpsAuthentication{TBuilder}"/>.
    /// </summary>
    public static WebApplicationBuilder AddIpsServer(this WebApplicationBuilder builder)
    {
        var url = builder.Configuration[$"{IpsServerSettings.Section}:{nameof(IpsServerSettings.Url)}"];
        if (!string.IsNullOrWhiteSpace(url))
        {
            builder.WebHost.UseUrls(url);
        }

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddAuthentication()
            .AddScheme<IpsServerOptions, IpsServerHandler>(Scheme, configureOptions: null);
        builder.Services.AddOptions<IpsServerOptions>(Scheme)
            .Configure<IConfiguration, TimeProvider>(Load)
            .ValidateOnStart();
        builder.Services.AddAuthorization();
        return builder;
    }

    /// <summary>Requires the "Server" authentication on these endpoints, for example <c>app.MapIpsCoreReceiver()</c>.</summary>
    public static TBuilder RequireIpsAuthentication<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new RequireIpsAuthenticationAttribute());

    private static void Load(IpsServerOptions options, IConfiguration configuration, TimeProvider timeProvider)
    {
        var settings = configuration.GetSection(IpsServerSettings.Section).Get<IpsServerSettings>() ?? new IpsServerSettings();
        options.ApiTokens = [.. settings.ApiTokens.Where(token => !string.IsNullOrWhiteSpace(token)).Select(token => token.Trim())];
        var certificates = settings.Signing.TrustedCertificates
            .Where(certificate => certificate.IsConfigured)
            .Select(certificate => certificate.Load())
            .ToArray();
        options.TrustedCertificates = certificates.Length;
        options.Verifier = certificates.Length == 0
            ? null
            : new IpsSignatureVerifier(certificates, Algorithms(settings.Signing.Algorithms), timeProvider);
    }

    private static IReadOnlyList<IpsSignatureAlgorithm> Algorithms(string[] names)
    {
        if (names.Length == 0)
        {
            return Enum.GetValues<IpsSignatureAlgorithm>();
        }

        var allowed = new List<IpsSignatureAlgorithm>();
        foreach (var name in names)
        {
            if (!IpsContentSignature.TryParse(name?.Trim(), out var algorithm))
            {
                throw new InvalidOperationException($"Server:Signing:Algorithms accepts RS256, PS256 and ES256, not '{name}'.");
            }

            allowed.Add(algorithm);
        }

        return allowed;
    }
}

// Requires what is configured: a listed token, a valid signature, or both. With nothing configured every request is
// accepted. The body is buffered, so model binding reads the same bytes that were hashed. A failure answers 401 without
// saying which check failed; the log has the reason, never the token.
internal sealed class IpsServerHandler(IOptionsMonitor<IpsServerOptions> options, ILoggerFactory loggerFactory, UrlEncoder encoder)
    : AuthenticationHandler<IpsServerOptions>(options, loggerFactory, encoder)
{
    private const string CertificateClaim = "ips:certificate";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Options.ApiTokens.Count != 0 && !IpsApiToken.Matches(Request.Headers.Authorization, Options.ApiTokens))
        {
            return Refuse("no valid API token", certificate: null);
        }

        string? certificate = null;
        if (Options.Verifier is { } verifier)
        {
            var signed = new IpsSignedRequest(
                Request.Headers[IpsSecurityHeaders.ContentSha256],
                Request.Headers[IpsSecurityHeaders.Signature],
                Request.Headers[IpsSecurityHeaders.SignatureAlgorithm],
                Request.Headers[IpsSecurityHeaders.Certificate]);
            var check = verifier.Verify(signed, await ReadBodyAsync());
            if (check != IpsSignatureCheck.Valid)
            {
                return Refuse($"signature check {check}", signed.Certificate);
            }

            certificate = signed.Certificate;
        }

        Claim[] claims = certificate is null ? [] : [new Claim(CertificateClaim, certificate)];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, IpsServerAuthentication.Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, IpsServerAuthentication.Scheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The request is not authenticated.")
            .ExecuteAsync(Context);

    private AuthenticateResult Refuse(string reason, string? certificate)
    {
        Logger.LogWarning("Refused {Method} {Path}: {Reason} (certificate {Certificate})",
            Request.Method, Request.Path, reason, certificate ?? "none");
        return AuthenticateResult.Fail(reason);
    }

    private async Task<byte[]> ReadBodyAsync()
    {
        Request.EnableBuffering();
        using var copy = new MemoryStream();
        await Request.Body.CopyToAsync(copy, Context.RequestAborted);
        Request.Body.Position = 0;
        return copy.ToArray();
    }
}
