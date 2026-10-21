using System.Net;
using System.Text.Json;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace IPS.Middleware.IntegrationTests;

public sealed class HostTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Host_starts_and_answers_liveness_without_external_dependencies(string environment)
    {
        using var factory = new MiddlewareFactory(environment);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Development_openapi_exposes_only_the_foundation_endpoint()
    {
        using var factory = new MiddlewareFactory("Development");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotNull(document.RootElement.GetProperty("openapi").GetString());
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name).ToArray();
        Assert.Equal("/health/live", Assert.Single(paths));
    }

    [Fact]
    public async Task Production_does_not_expose_openapi()
    {
        using var factory = new MiddlewareFactory("Production");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/ips/pacs008/send")]
    [InlineData("/api/ips/pacs009/send")]
    [InlineData("/api/ips/pacs004/send")]
    public async Task Foundation_does_not_accept_placeholder_payment_requests(string path)
    {
        using var factory = new MiddlewareFactory("Development");
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(path, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Every SQL command at Information was about 50 log entries per payment (013a); failed commands are still logged, as errors.
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Shipped_logging_writes_sql_commands_only_from_warning_and_keeps_the_service_at_information(string environment)
    {
        using var factory = new MiddlewareFactory(environment);
        var loggers = factory.Services.GetRequiredService<ILoggerFactory>();
        var commands = loggers.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");
        var service = loggers.CreateLogger("IPS.Middleware.Infrastructure.Payments.Execution.OutgoingRuntime");
        Assert.False(commands.IsEnabled(LogLevel.Information));
        Assert.True(commands.IsEnabled(LogLevel.Warning));
        Assert.True(service.IsEnabled(LogLevel.Information));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Custom")]
    public void Non_development_hosts_reject_explicit_unsigned_configuration(string environment)
    {
        using var factory = new SigningFactory(environment, allowUnsigned: true);
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("only be enabled in Development", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Development_host_uses_the_explicit_signing_configuration(bool enabled)
    {
        using var factory = new SigningFactory("Development", enabled);
        var policy = factory.Services.GetRequiredService<Pacs008SigningPolicy>();
        Assert.Equal(enabled, policy.AllowUnsignedWithoutCertificate);
        Assert.NotNull(factory.Services.GetRequiredService<Pacs008MessageSigner>());
    }

    private sealed class SigningFactory(string environment, bool allowUnsigned) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [Pacs008SigningPolicy.AllowUnsignedConfigurationKey] = allowUnsigned.ToString()
                }));
        }
    }

    private sealed class MiddlewareFactory(string environment) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment(environment);
    }
}
