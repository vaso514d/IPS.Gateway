using IPS.Middleware.Infrastructure.Inbound.Workers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

public sealed class IncomingWorkerConfigurationTests
{
    [Fact]
    public void Host_deadline_includes_configured_worker_drain_and_evidence_persistence()
    {
        using var factory = new ConfiguredHost(new()
        {
            ["Payments:Incoming:Workers:ShutdownBudget"] = "00:01:00"
        });
        Assert.False(factory.Services.GetRequiredService<IncomingWorkerOptions>().Enabled);
        var settings = factory.Services.GetRequiredService<IOptions<HostOptions>>().Value;
        Assert.Equal(TimeSpan.FromSeconds(62), settings.ShutdownTimeout);
        Assert.True(settings.ServicesStopConcurrently);
    }

    [Theory]
    [InlineData("Payments:Incoming:Workers:Enabled", "true")]
    [InlineData("Payments:Incoming:Workers:CbsFollowUpCapacity", "0")]
    [InlineData("Payments:Incoming:Workers:EmptyDelay", "00:00:00")]
    [InlineData("Payments:Incoming:Workers:MessageDelay", "-00:00:01")]
    [InlineData("Payments:Incoming:Workers:ErrorDelay", "00:00:00")]
    [InlineData("Payments:Incoming:Workers:ShutdownBudget", "00:00:00")]
    public void Invalid_worker_configuration_fails_before_starting(string key, string value)
    {
        using var factory = new ConfiguredHost(new()
        {
            [key] = value
        });
        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    private sealed class ConfiguredHost(Dictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        }
    }
}
