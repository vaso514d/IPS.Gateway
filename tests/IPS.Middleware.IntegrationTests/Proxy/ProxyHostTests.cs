using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IPS.Middleware.IntegrationTests.Transport;
using IPS.MiidleWear.Contracts.Proxy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;
using static IPS.Middleware.IntegrationTests.Proxy.ProxyFixture;

namespace IPS.Middleware.IntegrationTests.Proxy;

[Collection("Outgoing transport timing")]
public sealed class ProxyHostTests
{
    [Theory]
    [InlineData("/api/proxy/register")]
    [InlineData("/api/proxy/update")]
    [InlineData("/api/proxy/remove")]
    public async Task The_routes_do_not_exist_while_proxy_management_is_disabled(string path)
    {
        using var host = new ProxyHost(new Dictionary<string, string?>());
        using var client = host.CreateClient();

        using var response = await client.PostAsync(path, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Enabled_openapi_exposes_exactly_the_three_declared_operations()
    {
        using var host = new ProxyHost(Enabled("http://127.0.0.1:1"));
        using var client = host.CreateClient();

        var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal("RegisterProxy", paths.GetProperty("/api/proxy/register").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("UpdateProxy", paths.GetProperty("/api/proxy/update").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("RemoveProxy", paths.GetProperty("/api/proxy/remove").GetProperty("post").GetProperty("operationId").GetString());
    }

    [Fact]
    public async Task A_registration_is_signed_unsigned_sent_to_the_proxy_and_an_accept_is_a_200()
    {
        string? sent = null;
        await using var proxy = await HttpSimulator.StartAsync(async context =>
        {
            Assert.Equal("/PRX/register", context.Request.Path);
            sent = await new StreamReader(context.Request.Body).ReadToEndAsync();
            await context.Response.WriteAsync(Accepted(OperationId(sent)));
        });
        using var host = new ProxyHost(Enabled(proxy.Url));
        using var client = host.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/proxy/register", RegisterDto());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ProxyOperationResultDto>())!;
        Assert.Equal(new ProxyOperationResultDto(true, null, null), result);
        Assert.NotNull(sent);
        Middleware.Infrastructure.Proxy.ProxySchema.Validate(sent);
    }

    [Fact]
    public async Task A_reject_from_the_proxy_is_also_a_200_with_its_code_and_description()
    {
        await using var proxy = await HttpSimulator.StartAsync(async context =>
        {
            Assert.Equal("/PRX/remove", context.Request.Path);
            var sent = await new StreamReader(context.Request.Body).ReadToEndAsync();
            await context.Response.WriteAsync(Rejected(OperationId(sent), "AC01", "Account not found"));
        });
        using var host = new ProxyHost(Enabled(proxy.Url));
        using var client = host.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/proxy/remove", RemoveDto());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ProxyOperationResultDto(false, "AC01", "Account not found"), await response.Content.ReadFromJsonAsync<ProxyOperationResultDto>());
    }

    [Fact]
    public async Task An_update_goes_to_the_update_route()
    {
        var path = "";
        await using var proxy = await HttpSimulator.StartAsync(async context =>
        {
            path = context.Request.Path;
            var sent = await new StreamReader(context.Request.Body).ReadToEndAsync();
            await context.Response.WriteAsync(Accepted(OperationId(sent)));
        });
        using var host = new ProxyHost(Enabled(proxy.Url));
        using var client = host.CreateClient();
        var request = new ProxyUpdateRequestDto("01001011111", "Individual", null, null, null, null, null, null, null);

        using var response = await client.PostAsJsonAsync("/api/proxy/update", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/PRX/update", path);
    }

    [Fact]
    public async Task An_invalid_request_is_a_400_with_the_failing_fields_and_never_reaches_the_proxy()
    {
        var calls = 0;
        await using var proxy = await HttpSimulator.StartAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        });
        using var host = new ProxyHost(Enabled(proxy.Url));
        using var client = host.CreateClient();
        var request = RemoveDto() with { AccountIdentifier = "not-an-iban", AccountHolderType = "Company" };

        using var response = await client.PostAsJsonAsync("/api/proxy/remove", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("accountIdentifier", out _));
        Assert.True(errors.TryGetProperty("accountHolderType", out _));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task A_missing_account_holder_is_reported_as_a_validation_error()
    {
        using var host = new ProxyHost(Enabled("http://127.0.0.1:1"));
        using var client = host.CreateClient();

        using var response = await client.PostAsync("/api/proxy/register", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/proxy/register", "{\"accountHolder\":{\"identifier\":\"01001011111\",\"type\":\"LegalEntity\"}}")]
    [InlineData("/api/proxy/update", "{\"accountHolderIdentifier\":\"01001011111\",\"accountHolderType\":\"Individual\"}")]
    [InlineData("/api/proxy/remove", "{\"accountHolderIdentifier\":\"01001011111\",\"accountHolderType\":\"Individual\",\"accountIdentifier\":\"GE29NB0000000101904917\",\"accountCurrency\":\"GEL\",\"keepAccountActive\":false}")]
    public async Task A_minimal_request_with_only_the_required_fields_is_accepted_over_http(string path, string json)
    {
        await using var proxy = await HttpSimulator.StartAsync(async context =>
        {
            var sent = await new StreamReader(context.Request.Body).ReadToEndAsync();
            await context.Response.WriteAsync(Accepted(OperationId(sent)));
        });
        using var host = new ProxyHost(Enabled(proxy.Url));
        using var client = host.CreateClient();

        using var response = await client.PostAsync(path, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<ProxyOperationResultDto>())!.Accepted);
    }

    [Fact]
    public async Task A_proxy_error_status_is_a_502_and_a_slow_proxy_a_504_with_no_retry()
    {
        var calls = 0;
        var slow = false;
        await using var proxy = await HttpSimulator.StartAsync(async context =>
        {
            Interlocked.Increment(ref calls);
            if (slow)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, context.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                }

                return;
            }

            context.Response.StatusCode = 503;
        });
        var settings = Enabled(proxy.Url);
        settings["Proxy:Endpoint:RequestTimeout"] = "00:00:00.600";
        using var host = new ProxyHost(settings);
        using var client = host.CreateClient();

        using var failed = await client.PostAsJsonAsync("/api/proxy/register", RegisterDto());
        slow = true;
        using var timedOut = await client.PostAsJsonAsync("/api/proxy/register", RegisterDto());

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(HttpStatusCode.GatewayTimeout, timedOut.StatusCode);
        Assert.Equal("application/problem+json", timedOut.Content.Headers.ContentType!.MediaType);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void An_enabled_section_with_an_invalid_bic_stops_startup()
    {
        var settings = Enabled("http://127.0.0.1:1");
        settings["Proxy:ParticipantBic"] = "BAD";
        using var host = new ProxyHost(settings);

        var error = Assert.Throws<InvalidOperationException>(() => host.CreateClient());

        Assert.Contains("ParticipantBic", error.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, string?> Enabled(string url) => new()
    {
        ["Proxy:Enabled"] = "true",
        ["Proxy:ParticipantBic"] = Participant,
        ["Proxy:ProxyBic"] = ProxyBic,
        ["Proxy:Endpoint:BaseUrl"] = url,
        ["Proxy:Endpoint:ConnectTimeout"] = "00:00:00.200",
        ["Proxy:Endpoint:RequestTimeout"] = "00:00:05",
        ["Proxy:Endpoint:CheckCertificateRevocation"] = "false"
    };

    private sealed class ProxyHost(Dictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
    }
}
