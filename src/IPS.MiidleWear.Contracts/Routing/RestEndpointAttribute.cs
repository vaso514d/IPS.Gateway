namespace IPS.MiidleWear.Contracts.Routing;

/// <summary>
/// Declares the canonical REST endpoint of a contract operation — the dependency-free counterpart of ASP.NET's
/// <c>[HttpPost("…")]</c> + <c>[EndpointSummary("…")]</c>, placed on the interface itself so the route is part of the
/// contract rather than something each implementation writes by hand. The response type is the method's return type
/// (<c>Task&lt;T&gt;</c>), so no separate <c>[ProducesResponseType]</c> is needed.
/// <para>
/// The gateway builds its endpoints from these attributes (<c>IGatewayApi</c>); for <c>IClientPaymentReceiver</c> the
/// route is the default path the gateway calls on the core system.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RestEndpointAttribute : Attribute
{
    public RestEndpointAttribute(string httpMethod, string route)
    {
        HttpMethod = httpMethod;
        Route = route;
    }

    /// <summary>HTTP method, e.g. "POST".</summary>
    public string HttpMethod { get; }

    /// <summary>Canonical route, e.g. "/api/ips/pacs008/send".</summary>
    public string Route { get; }

    /// <summary>One-line summary shown in the OpenAPI document.</summary>
    public string? Summary { get; set; }

    /// <summary>Longer description shown in the OpenAPI document.</summary>
    public string? Description { get; set; }

    /// <summary>OpenAPI tag (Swagger group).</summary>
    public string? Tag { get; set; }

    /// <summary>ISO 20022 message the operation sends or receives, e.g. "pacs.008" — used in error codes and problem titles.</summary>
    public string? MessageType { get; set; }

    /// <summary>
    /// HTTP status of a successful call: 200 (default), or 202 for operations that only accept the work — the
    /// transaction is processed in the background and its final status is delivered or read later.
    /// </summary>
    public int SuccessStatusCode { get; set; } = 200;

    /// <summary>Central system the gateway talks to for this operation ("IPS" or "Proxy Solution") — used in problem titles.</summary>
    public string Upstream { get; set; } = "IPS";
}
