namespace IPS.MiidleWear.Security;

/// <summary>The HTTP headers of a signed request and of the static API token.</summary>
public static class IpsSecurityHeaders
{
    /// <summary>Base64 of the SHA-256 of the request body (44 characters).</summary>
    public const string ContentSha256 = "X-Ips-Content-SHA256";

    /// <summary>Base64 of the signature over the ASCII bytes of <see cref="ContentSha256"/>.</summary>
    public const string Signature = "X-Ips-Signature";

    /// <summary><c>RS256</c>, <c>PS256</c> or <c>ES256</c>.</summary>
    public const string SignatureAlgorithm = "X-Ips-Signature-Algorithm";

    /// <summary>The SHA-1 thumbprint (hex) of the signing certificate, which the receiver must trust.</summary>
    public const string Certificate = "X-Ips-Certificate";

    /// <summary>The header that carries the static API token.</summary>
    public const string Authorization = "Authorization";

    /// <summary>The scheme of the static API token: <c>Authorization: Bearer &lt;token&gt;</c>.</summary>
    public const string BearerScheme = "Bearer";
}
