using System.Security.Cryptography;
using System.Text;

namespace IPS.MiidleWear.Security;

/// <summary>The static API token: <c>Authorization: Bearer &lt;token&gt;</c>.</summary>
public static class IpsApiToken
{
    private const string Prefix = IpsSecurityHeaders.BearerScheme + " ";

    /// <summary>The <c>Authorization</c> header value for <paramref name="token"/>.</summary>
    public static string HeaderValue(string token) => Prefix + token;

    /// <summary>
    /// Whether <paramref name="authorization"/> carries one of <paramref name="tokens"/>. Each comparison takes constant
    /// time, so the response time does not reveal how much of a token matched.
    /// </summary>
    public static bool Matches(string? authorization, IEnumerable<string> tokens)
    {
        if (authorization is null || !authorization.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var presented = Encoding.UTF8.GetBytes(authorization[Prefix.Length..].Trim());
        var matched = false;
        foreach (var token in tokens)
        {
            matched |= CryptographicOperations.FixedTimeEquals(presented, Encoding.UTF8.GetBytes(token));
        }

        return matched;
    }
}

/// <summary>
/// Adds the static API token to every request of an <see cref="HttpClient"/>. Without a token (<c>null</c> or empty) it
/// adds nothing, for a receiver that is not protected.
/// </summary>
public sealed class IpsApiTokenHandler(string? token) : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Remove(IpsSecurityHeaders.Authorization);
            request.Headers.TryAddWithoutValidation(IpsSecurityHeaders.Authorization, IpsApiToken.HeaderValue(token));
        }

        return base.SendAsync(request, cancellationToken);
    }
}
