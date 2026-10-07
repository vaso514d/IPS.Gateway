using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Proxy;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Diagnostics;
using IPS.Middleware.Infrastructure.Proxy;
using IPS.Middleware.IntegrationTests.Payments;
using IPS.Middleware.IntegrationTests.Transport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Diagnostics;

[Collection("Metrics")]
public sealed class MetricsTests
{
    [Fact]
    public async Task A_committed_outgoing_send_counts_each_status_change_once_by_message_type()
    {
        using var probe = new MetricsProbe();
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptPain002Async();

        await core.ProcessAsync(id);

        var changes = probe.Of("ips.outgoing.status_changes");
        Assert.All(changes, change => Assert.Equal("pain.002", change.Tags["message_type"]));
        Assert.Equal(new[] { "accepted", "sending" }, changes.Select(change => (string)change.Tags["status"]!).Order().ToArray());
        Assert.All(changes, change => Assert.Equal(1, change.Value));
    }

    [Fact]
    public async Task A_save_that_does_not_commit_counts_nothing()
    {
        using var probe = new MetricsProbe();
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptPain002Async();
        var crash = new CrashOnSave(entry => entry.Entity.IsFinal);

        await Assert.ThrowsAsync<SimulatedCrash>(() => core.ProcessAsync(id, default, crash));

        Assert.Equal(new[] { "sending" }, probe.Of("ips.outgoing.status_changes").Select(change => (string)change.Tags["status"]!).ToArray());
    }

    [Theory]
    [InlineData(200, "RJCT/1017", "rejected")]
    [InlineData(503, "", "uncertain")]
    public async Task Rejected_and_uncertain_outcomes_are_counted_by_their_status(int httpStatus, string header, string expected)
    {
        using var probe = new MetricsProbe();
        await using var core = await ProcessingHarness.CreateAsync();
        core.Ips.Behavior = (reply, _) => httpStatus == 200
            ? core.Ips.RespondAsync(reply, header)
            : Task.FromResult(new IpsSubmissionResponse(httpStatus, "x", []));
        var id = await core.AcceptPain002Async();

        await core.ProcessAsync(id);

        Assert.Equal(1, probe.CountBy("ips.outgoing.status_changes", "status")[expected]);
    }

    [Fact]
    public void Incoming_registrations_and_core_outcomes_are_counted_by_kind_operation_and_result()
    {
        using var probe = new MetricsProbe();
        var at = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var transfer = IncomingTransfer.Register(Guid.NewGuid(), "BAGAGE22", "pacs.009", "E2E-1", at);
        transfer.BeginSubmission(at);
        transfer.RecordCoreResult(new CorePaymentResult(CoreOutcome.Accepted, at), at);

        CommittedEventMetrics.Record(transfer, transfer.PendingEvents);

        Assert.Equal(1, probe.CountBy("ips.incoming.registered", "kind")["pacs.009"]);
        var events = probe.Of("ips.incoming.core_events");
        Assert.Equal(2, events.Count);
        Assert.All(events, measured => Assert.Equal("pacs.009", measured.Tags["kind"]));
        Assert.Equal(new[] { "submissionstarted", "accepted" }, events.Select(measured => (string)measured.Tags["result"]!).ToArray());
        Assert.Equal(new[] { "submissionstarted", "coreoutcomerecorded" }, events.Select(measured => (string)measured.Tags["operation"]!).ToArray());
    }

    [Fact]
    public async Task Every_http_exchange_is_timed_by_client_and_result_and_a_resend_is_counted()
    {
        using var probe = new MetricsProbe();
        var status = 200;
        await using var server = await HttpSimulator.StartAsync(context =>
        {
            context.Response.StatusCode = status;
            return Task.CompletedTask;
        });
        using var certificates = new TransportCertificates();
        using var services = OutgoingHttpClientTests.Services(OutgoingHttpClientTests.Settings(server.Url, certificates));
        var ips = services.GetRequiredService<IIpsTransport>();

        await ips.SendAsync("<a />", default);
        status = 503;
        await ips.ResendAsync("<a />", default);

        var durations = probe.Of("ips.http.duration");
        Assert.Equal(new[] { "http_error", "ok" }, durations.Select(measured => (string)measured.Tags["result"]!).Order().ToArray());
        Assert.All(durations, measured => Assert.Equal("outgoing-ips", measured.Tags["client"]));
        Assert.Single(probe.Of("ips.resends"));
    }

    [Fact]
    public async Task A_failed_connection_and_a_timeout_are_timed_with_their_own_results()
    {
        using var probe = new MetricsProbe();
        string deadUrl;
        await using (var gone = await HttpSimulator.StartAsync(_ => Task.CompletedTask))
        {
            deadUrl = gone.Url;
        }

        using var certificates = new TransportCertificates();
        using (var refused = OutgoingHttpClientTests.Services(OutgoingHttpClientTests.Settings(deadUrl, certificates)))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => refused.GetRequiredService<IIpsTransport>().SendAsync("<a />", default));
        }

        await using var slow = await HttpSimulator.StartAsync(async context =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
            }
        });
        using (var timeout = OutgoingHttpClientTests.Services(OutgoingHttpClientTests.Settings(slow.Url, certificates, TimeSpan.FromMilliseconds(500))))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => timeout.GetRequiredService<IIpsTransport>().SendAsync("<a />", default));
        }

        var results = probe.Of("ips.http.duration").Select(measured => (string)measured.Tags["result"]!).Order().ToArray();
        Assert.Equal(2, results.Length);
        Assert.Contains("timeout", results);
        Assert.Contains(results, result => result is "failed" or "timeout");
    }

    [Fact]
    public async Task Proxy_calls_are_counted_by_operation_and_result_and_validation_rejections_by_operation()
    {
        using var probe = new MetricsProbe();
        var replies = new Queue<ProxyReply>([
            ProxyReply.Replied("accepted"),
            ProxyReply.Replied("rejected"),
            new ProxyReply(ProxyDelivery.TimedOut, null),
            new ProxyReply(ProxyDelivery.Failed, null)]);
        var management = new ProxyManagement(new Answers(), new Replies(replies));
        var request = new RegisterProxyRequest(
            new ProxyAccountHolder("01001011111", "Individual", null, null, null, null, null, null, null, null, null, null), null, null, null, null);

        for (var call = 0; call < 4; call++)
        {
            await management.RegisterAsync(request, default);
        }

        await management.RegisterAsync(request with { AccountHolder = null }, default);
        await management.RemoveAsync(new RemoveProxyRequest(null, null, null, null, false, null, null, null), default);

        Assert.Equal(
            new[] { "accepted", "failed", "rejected", "timed_out" },
            probe.Of("ips.proxy.calls").Select(measured => (string)measured.Tags["result"]!).Order().ToArray());
        Assert.All(probe.Of("ips.proxy.calls"), measured => Assert.Equal("register", measured.Tags["operation"]));
        Assert.Equal(new[] { "proxy.register", "proxy.remove" }, probe.Of("ips.validation.rejections").Select(measured => (string)measured.Tags["operation"]!).Order().ToArray());
    }

    [Fact]
    public async Task A_logged_failure_left_to_recovery_is_counted_by_component()
    {
        using var probe = new MetricsProbe();
        var work = new Infrastructure.Payments.Execution.SupervisedWork<Guid>(1, NullLogger.Instance);

        Assert.True(work.TryStart(Guid.NewGuid(), () => throw new InvalidOperationException("boom")));
        await work.StopAdmission();

        Assert.Equal(1, probe.CountBy("ips.errors", "component")["OutgoingWork"]);
    }

    private sealed class Answers : IProxyProtocol
    {
        public Task<string> PrepareRegisterAsync(RegisterProxyRequest request, ProxyIds ids, CancellationToken cancellationToken) => Task.FromResult("<x />");

        public Task<string> PrepareUpdateAsync(UpdateProxyRequest request, ProxyIds ids, CancellationToken cancellationToken) => Task.FromResult("<x />");

        public Task<string> PrepareRemoveAsync(RemoveProxyRequest request, ProxyIds ids, CancellationToken cancellationToken) => Task.FromResult("<x />");

        public ProxyOutcome ReadReply(string xml, string operationId) => xml == "accepted" ? ProxyOutcome.Accept() : ProxyOutcome.Reject("AM05", null);
    }

    private sealed class Replies(Queue<ProxyReply> replies) : IProxyClient
    {
        public Task<ProxyReply> SendAsync(ProxyOperation operation, string xml, CancellationToken cancellationToken) => Task.FromResult(replies.Dequeue());
    }
}
