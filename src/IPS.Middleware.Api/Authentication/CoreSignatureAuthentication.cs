using System.Security.Claims;
using System.Text.Encodings.Web;
using IPS.Middleware.Api.Configuration;
using IPS.MiidleWear.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace IPS.Middleware.Api.Authentication;

// Marks a controller or action the core system must sign (specification 021); anything not marked, such as the health
// endpoints, stays open.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class RequireCoreSignatureAttribute : AuthorizeAttribute
{
    public RequireCoreSignatureAttribute()
    {
        AuthenticationSchemes = CoreSignatureAuthentication.SchemeName;
    }
}

// Checks the core system's signature over the request body. Without trusted certificates every request is accepted, so the
// API is open as configured. The body is buffered, so model binding reads the same bytes that were hashed. A failure
// answers 401 without saying which check failed; the log has the reason.
internal sealed class CoreSignatureAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    CoreRequestSigning signing) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    internal const string SchemeName = "IpsSignature";
    private const string CertificateClaim = "ips:certificate";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (signing.Verifier is not { } verifier)
        {
            return Success(certificate: null);
        }

        var signed = new IpsSignedRequest(
            Request.Headers[IpsSecurityHeaders.ContentSha256],
            Request.Headers[IpsSecurityHeaders.Signature],
            Request.Headers[IpsSecurityHeaders.SignatureAlgorithm],
            Request.Headers[IpsSecurityHeaders.Certificate]);
        var check = verifier.Verify(signed, await ReadBodyAsync());
        if (check == IpsSignatureCheck.Valid)
        {
            return Success(signed.Certificate);
        }

        Logger.LogWarning("Refused {Method} {Path} from the core system: signature check {Check} (certificate {Certificate})",
            Request.Method, Request.Path, check, signed.Certificate ?? "none");
        return AuthenticateResult.Fail($"Signature check {check}.");
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "A valid request signature is required.")
            .ExecuteAsync(Context);

    private AuthenticateResult Success(string? certificate)
    {
        Claim[] claims = certificate is null ? [] : [new Claim(CertificateClaim, certificate)];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
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
