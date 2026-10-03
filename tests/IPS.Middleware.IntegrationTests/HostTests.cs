using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

    private sealed class MiddlewareFactory(string environment) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment(environment);
    }
}
