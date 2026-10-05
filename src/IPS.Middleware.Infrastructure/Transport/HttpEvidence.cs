using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Transport;

// One HTTP attempt whose status, complete body and every header value reach the interpreters unchanged.
internal static class HttpEvidence
{
    internal static async Task<(int Status, string Body, IEnumerable<(string Name, string Value)> Headers)> SendAsync(
        IHttpClientFactory clients,
        string name,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var client = clients.CreateClient(name);
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var headers = response.Headers.Concat(response.Content.Headers).Concat(response.TrailingHeaders)
            .SelectMany(header => header.Value.Select(value => (header.Key, value))).ToArray();
        return ((int)response.StatusCode, body, headers);
    }
}
