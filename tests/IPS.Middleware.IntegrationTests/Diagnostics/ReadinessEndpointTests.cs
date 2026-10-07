using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Diagnostics;

public sealed class ReadinessEndpointTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task A_host_with_every_feature_disabled_is_ready_and_still_live(string environment)
    {
        using var host = new Host(environment);
        using var client = host.CreateClient();

        using var ready = await client.GetAsync("/health/ready");
        using var live = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task An_unhealthy_check_is_a_503_with_only_the_status_and_liveness_is_unaffected()
    {
        using var host = new Host("Development", new FailingCheck(HealthStatus.Unhealthy, "secret detail about the database"));
        using var client = host.CreateClient();

        using var ready = await client.GetAsync("/health/ready");
        using var live = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("Unhealthy", await ready.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task A_degraded_check_is_still_a_200_and_says_so()
    {
        using var host = new Host("Development", new FailingCheck(HealthStatus.Degraded, "a certificate expires soon"));
        using var client = host.CreateClient();

        using var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Degraded", await ready.Content.ReadAsStringAsync());
    }

    private sealed class FailingCheck(HealthStatus status, string description) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthCheckResult(status, description));
    }

    private sealed class Host(string environment, IHealthCheck? extra = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            if (extra is not null)
            {
                builder.ConfigureServices(services => services.Configure<HealthCheckServiceOptions>(options =>
                    options.Registrations.Add(new HealthCheckRegistration("test", extra, null, ["ready"]))));
            }
        }
    }
}
