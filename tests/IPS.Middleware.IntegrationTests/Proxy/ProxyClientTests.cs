using IPS.Middleware.Application.Proxy;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Proxy;
using IPS.Middleware.Infrastructure.Transport;
using IPS.Middleware.IntegrationTests.Transport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Proxy;

[Collection("Outgoing transport timing")]
public sealed class ProxyClientTests
{
    [Theory]
    [InlineData(ProxyOperation.Register, "/prefix/PRX/register")]
    [InlineData(ProxyOperation.Update, "/prefix/PRX/update")]
    [InlineData(ProxyOperation.Remove, "/prefix/PRX/remove")]
    public async Task Each_operation_is_one_post_with_the_proxy_headers_and_the_exact_body(ProxyOperation operation, string path)
    {
        var calls = 0;
        const string xml = " <acmt>ქართული</acmt>\n";
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            Interlocked.Increment(ref calls);
            Assert.Equal("POST", context.Request.Method);
            Assert.Equal(path, context.Request.Path);
            Assert.Equal("TESTGE22", context.Request.Headers["X-MONTRAN-PRX-Channel"]);
            Assert.Equal("1", context.Request.Headers["X-MONTRAN-PRX-Version"]);
            Assert.Equal("keep-alive", context.Request.Headers.Connection);
            Assert.Equal("application/xml; charset=utf-8", context.Request.ContentType);
            Assert.Equal(xml, await new StreamReader(context.Request.Body).ReadToEndAsync());
            await context.Response.WriteAsync("<reply />");
        });
        using var services = Services(Settings(server.Url + "/prefix"));

        var reply = await services.GetRequiredService<IProxyClient>().SendAsync(operation, xml, default);

        Assert.Equal(new ProxyReply(ProxyDelivery.Replied, "<reply />"), reply);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(307)]
    [InlineData(400)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task A_status_that_is_not_success_is_no_answer_and_is_not_retried(int status)
    {
        var calls = 0;
        await using var server = await HttpSimulator.StartAsync(context =>
        {
            Interlocked.Increment(ref calls);
            context.Response.StatusCode = status;
            return context.Response.WriteAsync("<reply />");
        });
        using var services = Services(Settings(server.Url));

        var reply = await services.GetRequiredService<IProxyClient>().SendAsync(ProxyOperation.Register, "<x />", default);

        Assert.Equal(new ProxyReply(ProxyDelivery.Failed, null), reply);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_slow_proxy_is_a_timeout_after_one_attempt()
    {
        var calls = 0;
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            Interlocked.Increment(ref calls);
            try
            {
                await Task.Delay(Timeout.Infinite, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
            }
        });
        using var services = Services(Settings(server.Url, TimeSpan.FromMilliseconds(400)));

        var reply = await services.GetRequiredService<IProxyClient>().SendAsync(ProxyOperation.Update, "<x />", default);

        Assert.Equal(new ProxyReply(ProxyDelivery.TimedOut, null), reply);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task An_unreachable_proxy_is_no_answer()
    {
        string url;
        await using (var server = await HttpSimulator.StartAsync(_ => Task.CompletedTask))
        {
            url = server.Url;
        }

        using var services = Services(Settings(url));

        var reply = await services.GetRequiredService<IProxyClient>().SendAsync(ProxyOperation.Remove, "<x />", default);

        // A refused connection is a failure; a connect attempt that outlasts the connect timeout is a timeout. Neither is an answer.
        Assert.Null(reply.Xml);
        Assert.True(reply.Delivery is ProxyDelivery.Failed or ProxyDelivery.TimedOut);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_timeout()
    {
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
            }
        });
        using var services = Services(Settings(server.Url));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => services.GetRequiredService<IProxyClient>().SendAsync(ProxyOperation.Register, "<x />", cancellation.Token));
    }

    [Fact]
    public void Settings_require_valid_bics_a_version_and_a_client_certificate_outside_development()
    {
        var valid = new ProxySettings { Enabled = true, ParticipantBic = "TESTGE22", ProxyBic = "PROXGE22", Endpoint = new() { BaseUrl = "http://127.0.0.1:5000" } };
        valid.Validate(development: true);

        Assert.Throws<InvalidOperationException>(() => new ProxySettings { Enabled = true, ParticipantBic = "BAD", ProxyBic = "PROXGE22", Endpoint = valid.Endpoint }.Validate(true));
        Assert.Throws<InvalidOperationException>(() => new ProxySettings { Enabled = true, ParticipantBic = "TESTGE22", ProxyBic = "", Endpoint = valid.Endpoint }.Validate(true));
        Assert.Throws<InvalidOperationException>(() => new ProxySettings { Enabled = true, ParticipantBic = "TESTGE22", ProxyBic = "PROXGE22", ProtocolVersion = "", Endpoint = valid.Endpoint }.Validate(true));
        Assert.Throws<InvalidOperationException>(() => new ProxySettings { Enabled = true, ParticipantBic = "TESTGE22", ProxyBic = "PROXGE22", Endpoint = new() { BaseUrl = "https://proxy.example" } }.Validate(development: false));
        new ProxySettings().Validate(development: false);
    }

    private static ProxySettings Settings(string url, TimeSpan? timeout = null) => new()
    {
        Enabled = true,
        ParticipantBic = "TESTGE22",
        ProxyBic = "PROXGE22",
        Endpoint = new()
        {
            BaseUrl = url,
            ConnectTimeout = TimeSpan.FromMilliseconds(200),
            RequestTimeout = timeout ?? TimeSpan.FromSeconds(10),
            CircuitBreaker = new() { MinimumThroughput = 100 },
            CheckCertificateRevocation = false
        }
    };

    private static ServiceProvider Services(ProxySettings settings)
    {
        settings.Validate(development: true);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(settings);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new Pacs008SigningPolicy(false, false));
        services.AddSingleton<Pacs008MessageSigner>();
        services.AddProxyClient();
        return services.BuildServiceProvider();
    }
}
