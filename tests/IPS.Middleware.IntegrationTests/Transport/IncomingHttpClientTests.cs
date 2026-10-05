using System.Collections.Concurrent;
using System.Text.Json;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Transport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Transport;

public sealed class IncomingHttpClientTests
{
    private const string Participant = "TESTGE22";

    [Fact]
    public async Task Cbs_wire_contract_preserves_routes_json_idempotency_and_unsuccessful_evidence()
    {
        var requests = new ConcurrentQueue<(string Method, string Path, string Query, string Key, string ContentType, string Body)>();
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            requests.Enqueue((context.Request.Method, context.Request.Path.Value!, context.Request.QueryString.Value ?? "",
                context.Request.Headers["Idempotency-Key"].ToString(), context.Request.ContentType ?? "",
                await new StreamReader(context.Request.Body).ReadToEndAsync()));
            context.Response.StatusCode = 404;
            context.Response.Headers["X-Evidence"] = "source-value";
            await context.Response.WriteAsync("{\"unknown\":true}");
        });
        using var certificates = new TransportCertificates();
        using var services = Services(Settings(server.Url + "/prefix", certificates));
        var core = services.GetRequiredService<IIncomingCoreClient>();
        var response = await core.SubmitAsync(Participant, new Pacs008Request
        { EndToEndId = "unchanged-123", InstructionId = "instruction", Amount = 12.34m, Currency = "GEL" }, default);
        Assert.Equal(404, response.StatusCode);
        Assert.Equal("{\"unknown\":true}", response.Body);
        Assert.Contains(response.Headers, h => h.Name == "X-Evidence" && h.Value == "source-value");
        await core.QueryAsync(Participant, "a/b &?", default);
        var notification = new ReversalNotification(Guid.NewGuid(), Participant, "unchanged-123",
            DateTimeOffset.Parse("2026-01-02T03:04:05Z"), "MS03", "Frozen rejection", "original-message");
        await services.GetRequiredService<IIncomingReversalClient>().RequestAsync(notification, default);
        var captured = requests.ToArray();
        Assert.Equal(3, captured.Length);
        Assert.Equal(("POST", "/prefix/api/ips/pacs008/receive", "unchanged-123"),
            (captured[0].Method, captured[0].Path, captured[0].Key));
        Assert.StartsWith("application/json", captured[0].ContentType, StringComparison.Ordinal);
        using var submitted = JsonDocument.Parse(captured[0].Body);
        Assert.Equal("unchanged-123", submitted.RootElement.GetProperty("endToEndId").GetString());
        Assert.Equal(12.34m, submitted.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal("GET", captured[1].Method);
        Assert.Equal("/prefix/api/ips/payments/status", captured[1].Path);
        Assert.Equal("?messageKind=Pacs008&reference=a%2Fb%20%26%3F", captured[1].Query);
        Assert.Empty(captured[1].Key);
        Assert.Equal("/prefix/api/ips/transactions/status/receive", captured[2].Path);
        Assert.Equal("in:unchanged-123:Rejected", captured[2].Key);
        using var reversal = JsonDocument.Parse(captured[2].Body);
        Assert.Equal("unchanged-123", reversal.RootElement.GetProperty("coreReference").GetString());
        Assert.Equal(notification.PaymentId, reversal.RootElement.GetProperty("transactionId").GetGuid());
        Assert.Equal(notification.StatusAtUtc, reversal.RootElement.GetProperty("statusAtUtc").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(200, "raw <xml/> ", "42", "true")]
    [InlineData(200, "", "", "false")]
    [InlineData(503, "upstream failed", "invalid", "")]
    [InlineData(200, " <xml/>", "-7", "maybe")]
    public async Task Receive_preserves_unparsed_payload_status_and_transport_metadata(int status, string body, string sequence, string duplicate)
    {
        var calls = 0;
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            Interlocked.Increment(ref calls);
            Assert.Equal("GET", context.Request.Method);
            Assert.Equal("/Message", context.Request.Path);
            Assert.Equal(Participant, context.Request.Headers["X-MONTRAN-IPS-Channel"]);
            Assert.Equal("1", context.Request.Headers["X-MONTRAN-IPS-Version"]);
            context.Response.StatusCode = status;
            context.Response.Headers["X-MONTRAN-IPS-MessageType"] = "pacs.008.001.12";
            context.Response.Headers["X-MONTRAN-IPS-MessageSeq"] = sequence;
            context.Response.Headers["X-MONTRAN-IPS-PossibleDuplicate"] = duplicate;
            context.Response.Headers["X-MONTRAN-IPS-ReqSts"] = body.Length == 0 ? "EMPTY" : "OK";
            await context.Response.WriteAsync(body);
        });
        using var certificates = new TransportCertificates();
        using var services = Services(Settings(server.Url, certificates));
        var response = await services.GetRequiredService<IIncomingReceiveClient>().ReceiveAsync(default);
        Assert.Equal(status, response.Transport.HttpStatusCode);
        Assert.Equal(body, response.Transport.Body);
        Assert.Equal(long.TryParse(sequence, out var number) ? number : (long?)null, response.Sequence);
        Assert.Equal(duplicate != "false", response.PossibleDuplicate);
        Assert.Equal("pacs.008.001.12", response.MessageType);
        Assert.Equal(1, calls);
        Assert.Throws<NotSupportedException>(() => ((IList<IpsResponseHeader>)response.Transport.Headers).Clear());
    }

    [Theory]
    [InlineData(307)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Reply_sends_exact_utf8_xml_once_without_redirect_or_retry(int status)
    {
        var calls = 0;
        const string xml = " <message>ქართული</message>\n";
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            Interlocked.Increment(ref calls);
            Assert.Equal("POST", context.Request.Method);
            Assert.Equal("/Message", context.Request.Path);
            Assert.Equal("application/xml; charset=utf-8", context.Request.ContentType);
            Assert.Equal(xml, await new StreamReader(context.Request.Body).ReadToEndAsync());
            context.Response.StatusCode = status;
            context.Response.Headers.Location = "/redirected";
            await context.Response.WriteAsync("failure evidence");
        });
        using var certificates = new TransportCertificates();
        using var services = Services(Settings(server.Url, certificates));
        var response = await services.GetRequiredService<IIncomingReplyClient>().SendAsync(Participant, xml, default);
        Assert.Equal(status, response.HttpStatusCode);
        Assert.Equal("failure evidence", response.Body);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Slow_body_obeys_full_exchange_timeout_or_caller_cancellation(bool cancelCaller)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            Interlocked.Increment(ref calls);
            await context.Response.WriteAsync("partial");
            await context.Response.Body.FlushAsync();
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, context.RequestAborted); }
            catch (OperationCanceledException) { }
        });
        using var certificates = new TransportCertificates();
        using var services = Services(Settings(server.Url, certificates, timeout: TimeSpan.FromSeconds(1)));
        using var cancellation = new CancellationTokenSource();
        var call = services.GetRequiredService<IIncomingCoreClient>().QueryAsync(Participant, "original", cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancelCaller)
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        }
        else await Assert.ThrowsAsync<TimeoutRejectedException>(() => call);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Circuit_breaker_opens_without_manufacturing_business_responses_or_retrying()
    {
        var calls = 0;
        await using var server = await HttpSimulator.StartAsync(context =>
        {
            Interlocked.Increment(ref calls);
            context.Response.StatusCode = 503;
            return Task.CompletedTask;
        });
        using var certificates = new TransportCertificates();
        using var services = Services(Settings(server.Url, certificates));
        var client = services.GetRequiredService<IIncomingCoreClient>();
        Assert.Equal(503, (await client.QueryAsync(Participant, "original", default)).StatusCode);
        Assert.Equal(503, (await client.QueryAsync(Participant, "original", default)).StatusCode);
        await Assert.ThrowsAnyAsync<BrokenCircuitException>(() => client.QueryAsync(Participant, "original", default));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false, false, true, "pfx")]
    [InlineData(false, false, true, "pem")]
    [InlineData(false, false, true, "encrypted-pem")]
    [InlineData(true, false, false, "pfx")]
    [InlineData(false, true, false, "pfx")]
    public async Task Tls_requires_trusted_chain_matching_hostname_and_client_identity(bool wrongHost, bool untrusted, bool succeeds, string source)
    {
        using var certificates = new TransportCertificates();
        await using var server = await HttpSimulator.StartAsync(context => context.Response.WriteAsync("verified"),
            wrongHost ? certificates.Leaf(true, true) : certificates.Server, certificates.Client);
        using var services = Services(Settings(server.Url, certificates, tls: true, untrusted: untrusted, tlsIdentity: source == "pfx" ? certificates.Identity : certificates.SavePem(source == "encrypted-pem")));
        var call = services.GetRequiredService<IIncomingReplyClient>().SendAsync(Participant, "<test/>", default);
        if (succeeds) Assert.Equal("verified", (await call).Body);
        else await Assert.ThrowsAsync<HttpRequestException>(() => call);
    }

    internal static IncomingTransportSettings Settings(string url, TransportCertificates certificates, TimeSpan? timeout = null,
        bool tls = false, bool untrusted = false, CertificateSettings? tlsIdentity = null)
    {
        HttpEndpointSettings Endpoint() => new()
        {
            BaseUrl = url,
            ConnectTimeout = tls ? TimeSpan.FromSeconds(2) : TimeSpan.FromMilliseconds(200),
            RequestTimeout = timeout ?? TimeSpan.FromSeconds(10),
            CircuitBreaker = new() { MinimumThroughput = 2 },
            ClientCertificate = tls ? tlsIdentity ?? certificates.Identity : null,
            ServerTrust = tls && !untrusted ? [certificates.Trust] : [],
            CheckCertificateRevocation = false
        };
        return new()
        {
            Enabled = true,
            ParticipantBic = Participant,
            IpsSignatureTrust = [certificates.SignatureTrust],
            ReceiveTimeout = timeout ?? TimeSpan.FromSeconds(10),
            Ips = Endpoint(),
            Cbs = Endpoint()
        };
    }

    internal static ServiceProvider Services(IncomingTransportSettings settings)
    {
        settings.Validate(development: true);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(settings);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new Pacs008SigningPolicy(true, true));
        services.AddIncomingHttpClients();
        return services.BuildServiceProvider();
    }
}
