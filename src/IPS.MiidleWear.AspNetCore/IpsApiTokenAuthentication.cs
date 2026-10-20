using System.Security.Claims;
using System.Text.Encodings.Web;
using IPS.MiidleWear.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IPS.MiidleWear.AspNetCore;

/// <summary>The static API tokens the gateway may send (specification 021).</summary>
public sealed class IpsApiTokenOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The accepted tokens; list two while one is replaced. Without tokens (none, or only empty ones) every call is
    /// accepted, so the marked endpoints are open.
    /// </summary>
    public IList<string> Tokens { get; } = [];
}

/// <summary>
/// Marks a controller or action that requires the gateway's static API token, <c>Authorization: Bearer &lt;token&gt;</c>.
/// Endpoints that are not marked, such as health checks, stay open.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class RequireIpsApiTokenAttribute : AuthorizeAttribute
{
    /// <summary>Requires the <see cref="IpsApiTokenAuthentication.Scheme"/> scheme.</summary>
    public RequireIpsApiTokenAttribute()
    {
        AuthenticationSchemes = IpsApiTokenAuthentication.Scheme;
    }
}

/// <summary>Registration and endpoint conventions of the gateway's static API token.</summary>
public static class IpsApiTokenAuthentication
{
    /// <summary>The authentication scheme name.</summary>
    public const string Scheme = "IpsApiToken";

    /// <summary>
    /// Registers the <see cref="Scheme"/> authentication scheme with its tokens, and authorization. Mark the endpoints with
    /// <see cref="RequireIpsApiToken{TBuilder}"/> or <see cref="RequireIpsApiTokenAttribute"/>.
    /// </summary>
    /// <example><c>services.AddIpsApiTokenAuthentication(options => options.Tokens.Add(configuration["Receiver:ApiToken"]!));</c></example>
    public static IServiceCollection AddIpsApiTokenAuthentication(this IServiceCollection services, Action<IpsApiTokenOptions> configure)
    {
        services.AddAuthentication()
            .AddScheme<IpsApiTokenOptions, IpsApiTokenHandler>(Scheme, configure);
        services.AddAuthorization();
        return services;
    }

    /// <summary>
    /// Requires the gateway's API token on these endpoints: a call without one of the tokens answers 401, which the
    /// gateway treats as no answer and asks again.
    /// </summary>
    /// <example><c>app.MapIpsCoreReceiver().RequireIpsApiToken();</c></example>
    public static TBuilder RequireIpsApiToken<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new RequireIpsApiTokenAttribute());
}

internal sealed class IpsApiTokenHandler(IOptionsMonitor<IpsApiTokenOptions> options, ILoggerFactory loggerFactory, UrlEncoder encoder)
    : AuthenticationHandler<IpsApiTokenOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string[] tokens = [.. Options.Tokens.Where(token => !string.IsNullOrWhiteSpace(token)).Select(token => token.Trim())];
        if (tokens.Length == 0 || IpsApiToken.Matches(Request.Headers.Authorization, tokens))
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(IpsApiTokenAuthentication.Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, IpsApiTokenAuthentication.Scheme)));
        }

        Logger.LogWarning("Refused {Method} {Path}: no valid API token", Request.Method, Request.Path);
        return Task.FromResult(AuthenticateResult.Fail("No valid API token."));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "A valid API token is required.")
            .ExecuteAsync(Context);
}
