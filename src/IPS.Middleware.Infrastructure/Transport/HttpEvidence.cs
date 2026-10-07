using IPS.Middleware.Application.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Polly.Timeout;

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
        var started = PaymentMetrics.Start();
        var result = "failed";
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var headers = response.Headers.Concat(response.Content.Headers).Concat(response.TrailingHeaders)
                .SelectMany(header => header.Value.Select(value => (header.Key, value))).ToArray();
            result = response.IsSuccessStatusCode ? "ok" : "http_error";
            return ((int)response.StatusCode, body, headers);
        }
        catch (Exception error) when (error is TimeoutRejectedException || (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            result = "timeout";
            throw;
        }
        finally
        {
            PaymentMetrics.HttpExchanged(name, PaymentMetrics.Elapsed(started), result);
        }
    }
}
