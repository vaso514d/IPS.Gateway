namespace IPS.MiidleWear.Security.AspNetCore;

/// <summary>
/// The "Server" configuration section: where this service listens and what it requires of the requests to the endpoints
/// marked <see cref="RequireIpsAuthenticationAttribute"/>. What is configured is required; with nothing configured those
/// endpoints are open.
/// </summary>
/// <example>
/// <code>
/// "Server": {
///   "Url": "http://localhost:5280",
///   "ApiTokens": [ "token-1" ],
///   "Signing": { "TrustedCertificates": [ { "Thumbprint": "the other side's public certificate" } ] }
/// }
/// </code>
/// </example>
public sealed class IpsServerSettings
{
    /// <summary>The configuration section name.</summary>
    public const string Section = "Server";

    /// <summary>Where the service listens, for example <c>http://localhost:5280</c>; empty keeps the host's own setting.</summary>
    public string? Url { get; set; }

    /// <summary>The accepted static API tokens, <c>Authorization: Bearer &lt;token&gt;</c>; two while one is replaced.</summary>
    public string[] ApiTokens { get; set; } = [];

    /// <summary>The certificates whose signatures are accepted.</summary>
    public IpsServerSigningSettings Signing { get; set; } = new();
}

/// <summary>The public certificates of the callers whose signed requests this service accepts.</summary>
public sealed class IpsServerSigningSettings
{
    /// <summary>The callers' public certificates; two while one is replaced.</summary>
    public IpsCertificateSettings[] TrustedCertificates { get; set; } = [];

    /// <summary><c>RS256</c>, <c>PS256</c>, <c>ES256</c>; empty accepts all three.</summary>
    public string[] Algorithms { get; set; } = [];
}
