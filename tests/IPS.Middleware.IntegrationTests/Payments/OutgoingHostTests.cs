using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IPS.Middleware.Application.Payments.Execution;
using IPS.Middleware.Infrastructure.Payments.Execution;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.IntegrationTests.Transactions;
using IPS.MiidleWear.Contracts.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static IPS.Middleware.IntegrationTests.Payments.OutgoingHostFixture;

namespace IPS.Middleware.IntegrationTests.Payments;

public sealed class OutgoingHostTests
{
    private const string Send = "/api/ips/pacs008/send";
    private const string Pacs009Send = "/api/ips/pacs009/send";
    private const string Pacs004Send = "/api/ips/pacs004/send";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Final_outcomes_return_200_and_still_deliver_callbacks(bool rejected)
    {
        await using var fixture = await CreateAsync();
        fixture.Reject = rejected;
        using var host = fixture.Host();
        using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync(Send, Request());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = (await response.Content.ReadFromJsonAsync<TransactionStatusDto>())!;
        Assert.Equal(rejected ? TransactionStatus.Rejected : TransactionStatus.Accepted, status.Status);
        Assert.NotEqual(Guid.Empty, status.TransactionId);
        await EventuallyAsync(() => Task.FromResult(fixture.Callbacks.Count == 1));
        var callback = JsonSerializer.Deserialize<TransactionStatusDto>(fixture.Callbacks.Single(), JsonSerializerOptions.Web)!;
        Assert.Equal(status, callback);
        Assert.Single(fixture.Submissions);
        var query = await client.GetFromJsonAsync<TransactionStatusDto>("/api/ips/transactions/status?messageKind=Pacs008&clientReference=outgoing");
        Assert.Equal(status, query);
    }

    [Fact]
    public async Task Two_hosts_duplicate_submission_returns_immediately_and_disconnect_does_not_cancel_processing()
    {
        await using var fixture = await CreateAsync();
        fixture.Block = true;
        using var one = fixture.Host();
        using var two = fixture.Host();
        using var first = one.CreateClient();
        using var second = two.CreateClient();
        using var cancel = new CancellationTokenSource();
        var original = first.PostAsJsonAsync(Send, Request(), cancel.Token);
        await fixture.FirstSend.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var duplicate = await second.PostAsJsonAsync(Send, Request() with
        {
            Amount = -100
        });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(TransactionStatus.Processing, (await duplicate.Content.ReadFromJsonAsync<TransactionStatusDto>())!.Status);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
        fixture.Release.TrySetResult();
        await EventuallyAsync(() => Task.FromResult(fixture.Callbacks.Count == 1));
        Assert.Single(fixture.Submissions);
        var current = await second.GetFromJsonAsync<TransactionStatusDto>("/api/ips/transactions/status?messageKind=Pacs008&clientReference=outgoing");
        Assert.Equal(TransactionStatus.Accepted, current!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uncertain_result_waits_for_deadline_then_returns_504_without_resending(bool lost)
    {
        await using var fixture = await CreateAsync();
        fixture.LoseReply = lost;
        fixture.Unresolved = !lost;
        fixture.Configuration["Payments:Outgoing:Transport:Ips:ConnectTimeout"] = "00:00:00.050";
        fixture.Configuration["Payments:Outgoing:Transport:Ips:RequestTimeout"] = "00:00:00.200";
        fixture.Configuration["Payments:Outgoing:Execution:HttpWait"] = "00:00:00.400";
        fixture.Configuration["Payments:Outgoing:Execution:AttemptBudget"] = "00:00:01";
        using var host = fixture.Host();
        using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync(Send, Request());
        Assert.True(response.StatusCode == HttpStatusCode.GatewayTimeout, $"Expected 504, received {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var status = (await response.Content.ReadFromJsonAsync<TransactionStatusDto>())!;
        Assert.Equal(TransactionStatus.Processing, status.Status);
        Assert.NotEqual(Guid.Empty, status.TransactionId);
        var duplicate = await client.PostAsJsonAsync(Send, Request() with
        {
            Amount = -1
        });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Single(fixture.Submissions);
        await using var db = fixture.Database.Context();
        Assert.Equal(lost ? 1 : 2, await db.Set<OutgoingMessageRow>().CountAsync());
    }

    [Fact]
    public async Task Invalid_intake_and_status_queries_preserve_validation_and_not_found()
    {
        await using var fixture = await CreateAsync();
        using var host = fixture.Host();
        using var client = host.CreateClient();
        var invalid = await client.PostAsJsonAsync(Send, Request() with
        {
            Amount = -1
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var json = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("amount", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/ips/transactions/status")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/ips/transactions/status?messageKind=Pacs009&clientReference=missing")).StatusCode);
        await using var db = fixture.Database.Context();
        Assert.Empty(await db.Payments.ToListAsync());
        Assert.Empty(fixture.Submissions);
    }

    [Fact]
    public async Task Capacity_is_bounded_and_unadmitted_intake_is_recovered_after_caller_disconnect()
    {
        await using var fixture = await CreateAsync();
        fixture.Block = true;
        fixture.Configuration["Payments:Outgoing:Execution:Concurrency"] = "1";
        fixture.Configuration["Payments:Outgoing:Execution:ChannelCapacity"] = "1";
        fixture.Configuration["Payments:Outgoing:Execution:DiscoveryBatch"] = "1";
        using var host = fixture.Host();
        using var client = host.CreateClient();
        var first = client.PostAsJsonAsync(Send, Request("first"));
        await fixture.FirstSend.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var stop = new CancellationTokenSource();
        var second = client.PostAsJsonAsync(Send, Request("second"), stop.Token);
        await EventuallyAsync(async () => { await using var db = fixture.Database.Context(); return await db.Payments.CountAsync() == 2; });
        Assert.Single(fixture.Submissions);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        fixture.Release.TrySetResult();
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);
        await EventuallyAsync(() => Task.FromResult(fixture.Callbacks.Count == 2));
        Assert.Equal(2, fixture.Submissions.Count);
    }

    [Fact]
    public async Task Null_remittance_entries_validate_for_new_requests_but_do_not_break_duplicate_intake()
    {
        await using var fixture = await CreateAsync();
        fixture.Block = true;
        using var host = fixture.Host();
        using var client = host.CreateClient();
        var malformed = Request("invalid") with
        {
            Remittance = new IPS.MiidleWear.Contracts.Pacs008.Pacs008RemittanceDto { Structured = [null!] }
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Send, malformed)).StatusCode);
        using var cancel = new CancellationTokenSource();
        var initial = client.PostAsJsonAsync(Send, Request(), cancel.Token);
        await fixture.FirstSend.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var duplicate = await client.PostAsJsonAsync(Send, malformed with
        {
            ClientReference = "outgoing"
        });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(TransactionStatus.Processing, (await duplicate.Content.ReadFromJsonAsync<TransactionStatusDto>())!.Status);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initial);
        fixture.Release.TrySetResult();
    }

    [Fact]
    public async Task Runtime_waits_the_first_delay_then_investigates_and_sends_the_authorized_resend()
    {
        await using var fixture = await CreateAsync();
        fixture.AnswerUnresolved(1);
        fixture.InvestigationAnswer = "1016";
        fixture.Configuration["Payments:Outgoing:Investigation:DiscoveryInterval"] = "00:00:00.050";
        using var host = fixture.Host();
        using var client = host.CreateClient();
        await client.PostAsJsonAsync(Send, Request());

        // Poll SQL, not the status route: a status read acknowledges the outcome and would cancel its callback.
        await using var db = fixture.Database.Context();
        await EventuallyAsync(() => db.Payments.AsNoTracking().AnyAsync(p => p.CurrentStatus == Domain.Transactions.TransactionStatus.Accepted));

        Assert.True(fixture.FirstInvestigationAtUtc - fixture.FirstSendAtUtc >= TimeSpan.FromSeconds(9));
        var payments = fixture.PaymentSubmissions;
        Assert.Equal(2, payments.Length);
        Assert.Equal(payments[0], payments[1]);
        await EventuallyAsync(() => Task.FromResult(!fixture.Callbacks.IsEmpty));
    }

    [Fact]
    public async Task Shipped_timeouts_start_with_execution_enabled()
    {
        await using var fixture = await CreateAsync();
        fixture.Configuration.Remove("Payments:Outgoing:Transport:Ips:RequestTimeout");
        fixture.Configuration.Remove("Payments:Outgoing:Execution:HttpWait");
        fixture.Configuration.Remove("Payments:Outgoing:Execution:AttemptBudget");
        using var host = fixture.Host();
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Pacs009_send_returns_the_final_status_and_reports_validation_errors()
    {
        await using var fixture = await CreateAsync();
        using var host = fixture.Host();
        using var client = host.CreateClient();

        var invalid = await client.PostAsJsonAsync(Pacs009Send, Pacs009Request("bad") with { Amount = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var response = await client.PostAsJsonAsync(Pacs009Send, Pacs009Request());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = (await response.Content.ReadFromJsonAsync<TransactionStatusDto>())!;
        Assert.Equal(TransactionStatus.Accepted, status.Status);
        Assert.Equal(IPS.MiidleWear.Contracts.Transactions.IpsMessageKind.Pacs009, status.MessageKind);
        await EventuallyAsync(() => Task.FromResult(fixture.Callbacks.Count == 1));
        Assert.Equal([false], fixture.PossibleDuplicateFlags);
        var query = await client.GetFromJsonAsync<TransactionStatusDto>("/api/ips/transactions/status?messageKind=Pacs009&clientReference=outgoing9");
        Assert.Equal(status, query);
    }

    [Fact]
    public async Task Pacs009_uncertain_result_returns_504_and_a_duplicate_returns_the_current_status_at_once()
    {
        await using var fixture = await CreateAsync();
        fixture.LoseReply = true;
        fixture.Configuration["Payments:Outgoing:Transport:Ips:ConnectTimeout"] = "00:00:00.050";
        fixture.Configuration["Payments:Outgoing:Transport:Ips:RequestTimeout"] = "00:00:00.200";
        fixture.Configuration["Payments:Outgoing:Execution:HttpWait"] = "00:00:00.400";
        fixture.Configuration["Payments:Outgoing:Execution:AttemptBudget"] = "00:00:01";
        using var host = fixture.Host();
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(Pacs009Send, Pacs009Request());

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        var status = (await response.Content.ReadFromJsonAsync<TransactionStatusDto>())!;
        Assert.Equal(TransactionStatus.Processing, status.Status);
        var duplicate = await client.PostAsJsonAsync(Pacs009Send, Pacs009Request() with { Amount = -1 });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Single(fixture.PaymentSubmissions);
    }

    [Fact]
    public async Task Pacs009_lost_reply_is_recovered_by_a_possible_duplicate_resend_of_the_same_bytes()
    {
        await using var fixture = await CreateAsync();
        fixture.LoseNextReplies(1);
        fixture.Configuration["Payments:Outgoing:Investigation:DiscoveryInterval"] = "00:00:00.050";
        using var host = fixture.Host();
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(Pacs009Send, Pacs009Request());
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.GatewayTimeout);

        await using var db = fixture.Database.Context();
        await EventuallyAsync(() => db.Payments.AsNoTracking().AnyAsync(p => p.CurrentStatus == Domain.Transactions.TransactionStatus.Accepted));
        await EventuallyAsync(() => Task.FromResult(fixture.Callbacks.Count == 1));
        Assert.Equal([false, true], fixture.PossibleDuplicateFlags);
        var sent = fixture.PaymentSubmissions;
        Assert.Equal(2, sent.Length);
        Assert.Equal(sent[0], sent[1]);
    }

    [Fact]
    public async Task Pacs004_send_returns_the_final_status_and_reports_validation_errors()
    {
        await using var fixture = await CreateAsync();
        using var host = fixture.Host();
        using var client = host.CreateClient();

        var invalid = await client.PostAsJsonAsync(Pacs004Send, Pacs004Request("bad") with { ReturnReasonCode = "AC03" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var response = await client.PostAsJsonAsync(Pacs004Send, Pacs004Request());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = (await response.Content.ReadFromJsonAsync<TransactionStatusDto>())!;
        Assert.Equal(TransactionStatus.Accepted, status.Status);
        Assert.Equal(IPS.MiidleWear.Contracts.Transactions.IpsMessageKind.Pacs004, status.MessageKind);
        await EventuallyAsync(() => Task.FromResult(fixture.Callbacks.Count == 1));
        Assert.Equal([false], fixture.PossibleDuplicateFlags);
        var query = await client.GetFromJsonAsync<TransactionStatusDto>("/api/ips/transactions/status?messageKind=Pacs004&clientReference=outgoing4");
        Assert.Equal(status, query);
    }

    [Fact]
    public async Task Pacs004_uncertain_result_returns_504_and_a_duplicate_returns_the_current_status_at_once()
    {
        await using var fixture = await CreateAsync();
        fixture.LoseReply = true;
        fixture.Configuration["Payments:Outgoing:Transport:Ips:ConnectTimeout"] = "00:00:00.050";
        fixture.Configuration["Payments:Outgoing:Transport:Ips:RequestTimeout"] = "00:00:00.200";
        fixture.Configuration["Payments:Outgoing:Execution:HttpWait"] = "00:00:00.400";
        fixture.Configuration["Payments:Outgoing:Execution:AttemptBudget"] = "00:00:01";
        using var host = fixture.Host();
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(Pacs004Send, Pacs004Request());

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        var status = (await response.Content.ReadFromJsonAsync<TransactionStatusDto>())!;
        Assert.Equal(TransactionStatus.Processing, status.Status);
        var duplicate = await client.PostAsJsonAsync(Pacs004Send, Pacs004Request() with { Amount = -1 });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Single(fixture.PaymentSubmissions);
    }

    [Fact]
    public async Task Pacs004_lost_reply_is_recovered_by_a_possible_duplicate_resend_of_the_same_bytes()
    {
        await using var fixture = await CreateAsync();
        fixture.LoseNextReplies(1);
        fixture.Configuration["Payments:Outgoing:Investigation:DiscoveryInterval"] = "00:00:00.050";
        using var host = fixture.Host();
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(Pacs004Send, Pacs004Request());
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.GatewayTimeout);

        await using var db = fixture.Database.Context();
        await EventuallyAsync(() => db.Payments.AsNoTracking().AnyAsync(p => p.CurrentStatus == Domain.Transactions.TransactionStatus.Accepted));
        await EventuallyAsync(() => Task.FromResult(fixture.Callbacks.Count == 1));
        Assert.Equal([false, true], fixture.PossibleDuplicateFlags);
        var sent = fixture.PaymentSubmissions;
        Assert.Equal(2, sent.Length);
        Assert.Equal(sent[0], sent[1]);
    }

    [Fact]
    public async Task Stop_after_dispose_is_safe_and_does_not_admit_work()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var runtime = new OutgoingRuntime(services.GetRequiredService<IServiceScopeFactory>(), new(enabled: true), new(), new(),
            TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<OutgoingRuntime>.Instance);
        runtime.Dispose();
        await runtime.StopAsync(default);
        Assert.False(runtime.TryStart(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("Payments:Outgoing:Investigation:MaxCycles", "-1")]
    [InlineData("Payments:Outgoing:Investigation:MaxResends", "-1")]
    [InlineData("Payments:Outgoing:Investigation:Ownership", "00:00:37")]
    [InlineData("Payments:Outgoing:Transport:Enabled", "false")]
    [InlineData("Payments:Outgoing:Execution:Concurrency", "101")]
    [InlineData("Payments:Outgoing:Execution:CallbackConcurrency", "101")]
    [InlineData("Payments:Outgoing:Execution:DiscoveryBatch", "257")]
    [InlineData("Payments:Outgoing:Execution:HttpWait", "00:00:09")]
    [InlineData("Payments:Outgoing:Execution:AttemptBudget", "00:00:44")]
    [InlineData("Payments:Outgoing:Execution:ShutdownBudget", "00:00:00")]
    [InlineData("Payments:Outgoing:Protocol:IpsBic", "bad")]
    [InlineData("Payments:Outgoing:Policy:Currencies:0:Code", "bad")]
    public async Task Startup_rejects_invalid_execution_policy_and_capacity(string key, string value)
    {
        await using var fixture = await CreateAsync();
        fixture.Configuration[key] = value;
        using var host = fixture.Host();
        Assert.ThrowsAny<Exception>(() => host.CreateClient());
        Assert.Empty(fixture.Submissions);
    }

    [Fact]
    public async Task Shutdown_cancels_after_drain_and_leaves_submission_marker_for_recovery()
    {
        await using var fixture = await CreateAsync();
        fixture.Block = true;
        using var host = fixture.Host();
        using var client = host.CreateClient();
        using var caller = new CancellationTokenSource();
        var request = client.PostAsJsonAsync(Send, Request(), caller.Token);
        await fixture.FirstSend.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var runtime = host.Services.GetRequiredService<OutgoingRuntime>();
        await runtime.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(runtime.TryStart(Guid.NewGuid()));
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await using var db = fixture.Database.Context();
        var marker = Assert.Single(await db.Set<OutgoingMessageRow>().ToListAsync());
        Assert.NotNull(marker.StartedAtUtc);
    }
}
