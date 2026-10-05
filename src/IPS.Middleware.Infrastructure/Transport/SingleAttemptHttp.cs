using System.Net;
using System.Net.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace IPS.Middleware.Infrastructure.Transport;

internal sealed record HttpClientProfile(HttpEndpointSettings Endpoint, int Connections, TimeSpan Timeout, SslClientAuthenticationOptions Tls);

internal static class SingleAttemptHttp
{
    internal static void Add(IServiceCollection services, string name, Func<IServiceProvider, HttpClientProfile> profile)
    {
        var client = services.AddHttpClient(name, (sp, http) =>
        {
            http.BaseAddress = new Uri(profile(sp).Endpoint.BaseUrl.TrimEnd('/') + '/');
            http.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var endpoint = profile(sp).Endpoint;
            var connections = profile(sp).Connections;
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip,
                MaxConnectionsPerServer = connections,
                ConnectTimeout = endpoint.ConnectTimeout,
                PooledConnectionLifetime = endpoint.PooledConnectionLifetime,
                PooledConnectionIdleTimeout = endpoint.PooledConnectionIdleTimeout,
                SslOptions = profile(sp).Tls
            };
        }).SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        client.AddResilienceHandler("single-attempt", (pipeline, context) =>
        {
            var endpoint = profile(context.ServiceProvider).Endpoint;
            var breaker = endpoint.CircuitBreaker;
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = breaker.FailureRatio,
                MinimumThroughput = breaker.MinimumThroughput,
                SamplingDuration = breaker.SamplingDuration,
                BreakDuration = breaker.BreakDuration
            }).AddTimeout(new HttpTimeoutStrategyOptions
            {
                Timeout = profile(context.ServiceProvider).Timeout
            });
        });
        // Buffer inside resilience so its timeout and circuit breaker include response-body failures.
        client.AddHttpMessageHandler(() => new ResponseBodyHandler());
    }

    private sealed class ResponseBodyHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            try
            {
                await response.Content.LoadIntoBufferAsync(cancellationToken);
                return response;
            }
            catch { response.Dispose(); throw; }
        }
    }
}
