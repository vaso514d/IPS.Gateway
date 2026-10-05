using System.IO.Compression;
using System.Text;
using System.Text.Json;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Payments.Transport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Polly.Timeout;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Transport;

[Collection("Outgoing transport timing")]
public sealed class OutgoingHttpClientTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(307)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Initial_send_preserves_exact_wire_and_all_unsuccessful_evidence_without_retry(int code)
    {
        var calls = 0;
        const string xml = " <payment>ქართული</payment>\n";
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            Interlocked.Increment(ref calls);
            Assert.Equal("POST", context.Request.Method);
            Assert.Equal("/prefix/Message", context.Request.Path);
            Assert.Equal("TESTGE22", context.Request.Headers["X-MONTRAN-IPS-Channel"]);
            Assert.Equal("1", context.Request.Headers["X-MONTRAN-IPS-Version"]);
            Assert.Equal("keep-alive", context.Request.Headers.Connection);
            Assert.False(context.Request.Headers.ContainsKey("X-MONTRAN-RTP-PossibleDuplicate"));
            Assert.Equal("application/xml; charset=utf-8", context.Request.ContentType);
            Assert.Equal(xml, await new StreamReader(context.Request.Body).ReadToEndAsync());
            context.Response.StatusCode = code;
            context.Response.Headers.Location = "/redirected";
            context.Response.Headers["X-Evidence"] = new Microsoft.Extensions.Primitives.StringValues(["one", "two"]);
            context.Response.Headers.ContentEncoding = "gzip";
            await using var gzip = new GZipStream(context.Response.Body, CompressionMode.Compress, leaveOpen: true);
            await gzip.WriteAsync(Encoding.UTF8.GetBytes("malformed <response>"));
        });
        using var certificates = new TransportCertificates();
        using var services = Services(Settings(server.Url + "/prefix", certificates));
        var response = await services.GetRequiredService<IIpsTransport>().SendAsync(xml, default);
        Assert.Equal(code, response.HttpStatusCode);
        Assert.Equal("malformed <response>", response.Body);
        Assert.Contains(response.Headers, h => h.Name == "X-Evidence" && h.Value == "one");
        Assert.Contains(response.Headers, h => h.Name == "X-Evidence" && h.Value == "two");
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(204)]
    [InlineData(503)]
    public async Task Callback_uses_frozen_status_json_and_idempotency_key_once(int code)
    {
        var calls = 0;
        var status = new OutgoingStatus(Guid.NewGuid(), 3, "pacs.008", "original", TransactionStatus.Rejected,
            DateTimeOffset.Parse("2026-10-05T00:00:00Z"), new("AC01", 1009, "Frozen rejection"), "message", "end-to-end");
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            Interlocked.Increment(ref calls);
            Assert.Equal("POST", context.Request.Method);
            Assert.Equal("/prefix/api/ips/transactions/status/receive", context.Request.Path);
            Assert.Equal("original:Rejected", context.Request.Headers["Idempotency-Key"]);
            Assert.StartsWith("application/json", context.Request.ContentType, StringComparison.Ordinal);
            using var json = await JsonDocument.ParseAsync(context.Request.Body);
            var body = json.RootElement;
            Assert.Equal(status.PaymentId, body.GetProperty("transactionId").GetGuid());
            Assert.Equal(status.StatusAtUtc, body.GetProperty("statusAtUtc").GetDateTimeOffset());
            Assert.Equal("original", body.GetProperty("clientReference").GetString());
            Assert.Equal("message", body.GetProperty("messageId").GetString());
            Assert.Equal("end-to-end", body.GetProperty("endToEndId").GetString());
            Assert.Equal("AC01", body.GetProperty("reasonCode").GetString());
            context.Response.StatusCode = code;
        });
        using var certificates = new TransportCertificates();
        using var services = Services(Settings(server.Url + "/prefix", certificates));
        Assert.Equal(code, await services.GetRequiredService<IOutgoingStatusReceiver>().SendAsync(status, status.IdempotencyKey, default));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Body_read_is_bounded_by_timeout_and_caller_cancellation(bool cancel, bool callback)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            Interlocked.Increment(ref calls);
            await context.Response.WriteAsync("partial");
            await context.Response.Body.FlushAsync();
            started.SetResult();
            try
            { await Task.Delay(Timeout.Infinite, context.RequestAborted); }
            catch (OperationCanceledException) { }
        });
        using var certificates = new TransportCertificates();
        using var services = Services(Settings(server.Url, certificates, TimeSpan.FromSeconds(1)));
        using var stop = new CancellationTokenSource();
        var status = new OutgoingStatus(Guid.NewGuid(), 3, "pacs.008", "original", TransactionStatus.Accepted, DateTimeOffset.UtcNow, new(), "message", "e2e");
        Task call = callback ? services.GetRequiredService<IOutgoingStatusReceiver>().SendAsync(status, status.IdempotencyKey, stop.Token)
            : services.GetRequiredService<IIpsTransport>().SendAsync("<payment/>", stop.Token);
        var first = await Task.WhenAny(started.Task, call).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(first == started.Task, $"Request ended before response-body handshake: calls={calls}, status={call.Status}, error={call.Exception}");
        await started.Task;
        if (cancel)
        {
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        }
        else
        {
            await Assert.ThrowsAsync<TimeoutRejectedException>(() => call);
        }

        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Outgoing_pool_enforces_hostname_and_client_certificate(bool wrongHost)
    {
        using var certificates = new TransportCertificates();
        await using var server = await HttpSimulator.StartAsync(context => context.Response.WriteAsync("verified"),
            wrongHost ? certificates.Leaf(true, true) : certificates.Server, certificates.Client);
        using var services = Services(Settings(server.Url, certificates, tls: true));
        var call = services.GetRequiredService<IIpsTransport>().SendAsync("<payment/>", default);
        if (wrongHost)
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => call);
        }
        else
        {
            Assert.Equal("verified", (await call).Body);
        }
    }

    internal static OutgoingTransportSettings Settings(string url, TransportCertificates certificates, TimeSpan? timeout = null, bool tls = false)
    {
        var incoming = IncomingHttpClientTests.Settings(url, certificates, timeout, tls);
        return new()
        {
            Enabled = true,
            ParticipantBic = "TESTGE22",
            Ips = incoming.Ips,
            Cbs = incoming.Cbs,
            IpsSignatureTrust = incoming.IpsSignatureTrust
        };
    }
    internal static ServiceProvider Services(OutgoingTransportSettings settings)
    {
        settings.Validate(true);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(settings);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new Pacs008SigningPolicy(true, true));
        services.AddOutgoingHttpClients();
        return services.BuildServiceProvider();
    }
}

// Keep wall-clock body-timeout tests out of parallel SQL/process startup load, so they reach the body phase.
[CollectionDefinition("Outgoing transport timing", DisableParallelization = true)]
public sealed class OutgoingTransportTimingCollection;
