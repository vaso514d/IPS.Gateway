using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pain002;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Execution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;
using static IPS.Middleware.IntegrationTests.Payments.OutgoingHostFixture;

namespace IPS.Middleware.IntegrationTests.Payments;

// Several instances share one database and one IPS: SQL claims decide who works, so each payment is sent and reported once, an
// instance that stops hands its work over, and a stop that outlasts its budget leaves a marker the next instance recovers.
// Every scenario uses pain.002, whose recovery is the possible-duplicate resend shared by every outgoing type except pacs.008
// (which investigates with a pacs.028 first; its claim path is the same SQL claim and is proved by the workflow tests).
[Collection("Outgoing transport timing")]
public sealed class OutgoingMultiInstanceTests
{
    private const string Send = "/api/ips/pain002/send";

    // How long "nothing more arrives" is observed; the proof is the durable state checked around it.
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(750);

    [Fact]
    public async Task Two_instances_recovering_the_same_work_each_send_part_of_it_and_every_payment_goes_out_and_is_reported_once()
    {
        await using var fixture = await CreateAsync();
        // Many sends at once make the simulator, which signs each reply with Java, slow; the budgets leave it room so that no
        // outcome is unknown and every send in this scenario is a first send.
        fixture.Configuration["Payments:Outgoing:Transport:Ips:RequestTimeout"] = "00:00:30";
        fixture.Configuration["Payments:Outgoing:Execution:HttpWait"] = "00:00:31";
        fixture.Configuration["Payments:Outgoing:Execution:AttemptBudget"] = "00:00:32";
        fixture.Configuration["Payments:Outgoing:Pacs008:Ownership"] = "00:00:40";
        fixture.Configuration["Payments:Outgoing:Execution:Concurrency"] = "4";
        const int payments = 40;
        // The two instances tell IPS apart by the protocol version header they send, so who sent what is visible.
        using var first = fixture.Host(new() { ["Payments:Outgoing:Transport:IpsVersion"] = "1" });
        using var second = fixture.Host(new() { ["Payments:Outgoing:Transport:IpsVersion"] = "2" });
        using var firstClient = first.CreateClient();
        using var secondClient = second.CreateClient();
        for (var number = 0; number < payments; number++)
        {
            await SeedAsync(fixture, $"race-{number}");
        }

        await EventuallyAsync(() => Task.FromResult(fixture.Callbacks.Count >= payments));
        await Task.Delay(Settle);

        Assert.Equal(payments, fixture.Submissions.Count);
        Assert.Equal(payments, fixture.Submissions.Distinct().Count());
        Assert.All(fixture.PossibleDuplicateFlags, flagged => Assert.False(flagged));
        var byInstance = fixture.IpsVersions.GroupBy(version => version ?? "").ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(new[] { "1", "2" }, byInstance.Keys.Order());
        Assert.Equal(payments, byInstance.Values.Sum());
        Assert.Equal(payments, fixture.Callbacks.Count);
        Assert.Equal(payments, fixture.Callbacks.Select(ClientReferenceOf).Distinct().Count());
        await using var db = fixture.Database.Context();
        Assert.All(await db.Payments.AsNoTracking().ToListAsync(), payment => Assert.Equal(TransactionStatus.Accepted, payment.CurrentStatus));
        Assert.DoesNotContain(await db.OutgoingMetadata.AsNoTracking().ToListAsync(), metadata => metadata.ClaimToken != null);
        Assert.Equal(payments, await db.OutgoingStatusDeliveries.AsNoTracking().CountAsync(row => row.State == StatusDeliveryState.Delivered));
    }

    [Fact]
    public async Task A_stop_inside_the_budget_finishes_the_send_commits_its_outcome_and_refuses_new_work_while_readiness_turns_unhealthy()
    {
        await using var fixture = await CreateAsync();
        fixture.Configuration["Payments:Outgoing:Execution:ShutdownBudget"] = "00:00:08";
        fixture.Block = true;
        await SeedAsync(fixture, "drain-in-budget");
        using var first = fixture.Host();
        using var client = first.CreateClient();
        await fixture.FirstSend.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var runtime = first.Services.GetRequiredService<OutgoingRuntime>();
        using (var before = await client.GetAsync("/health/ready"))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        var stopping = runtime.StopAsync(default);
        var refused = !runtime.TryStart(Guid.NewGuid());
        using var readiness = await client.GetAsync("/health/ready");
        fixture.Release.TrySetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.True(refused);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        await using (var db = fixture.Database.Context())
        {
            var payment = Assert.Single(await db.Payments.AsNoTracking().ToListAsync());
            Assert.Equal(TransactionStatus.Accepted, payment.CurrentStatus);
            Assert.DoesNotContain(await db.OutgoingMetadata.AsNoTracking().ToListAsync(), metadata => metadata.ClaimToken != null);
            Assert.Equal(StatusDeliveryState.Pending, Assert.Single(await db.OutgoingStatusDeliveries.AsNoTracking().ToListAsync()).State);
        }

        Assert.Empty(fixture.Callbacks);
        Assert.Single(fixture.Submissions);

        // The callback it committed is delivered by the next instance, once.
        fixture.Block = false;
        using var second = fixture.Host();
        using var secondClient = second.CreateClient();
        await EventuallyAsync(() => Task.FromResult(fixture.Callbacks.Count == 1));
        await Task.Delay(Settle);
        Assert.Single(fixture.Callbacks);
        Assert.Single(fixture.Submissions);
    }

    [Fact]
    public async Task A_stop_that_outlasts_the_budget_leaves_a_claim_and_a_marker_and_the_next_instance_completes_it_with_one_flagged_resend_of_the_same_bytes()
    {
        await using var fixture = await CreateAsync();
        fixture.Configuration["Payments:Outgoing:Execution:ShutdownBudget"] = "00:00:00.500";
        fixture.Block = true;
        await SeedAsync(fixture, "drain-over-budget");
        using var first = fixture.Host();
        using var client = first.CreateClient();
        await fixture.FirstSend.Task.WaitAsync(TimeSpan.FromSeconds(15));

        await first.Services.GetRequiredService<OutgoingRuntime>().StopAsync(default).WaitAsync(TimeSpan.FromSeconds(15));

        await using (var db = fixture.Database.Context())
        {
            var payment = Assert.Single(await db.Payments.AsNoTracking().ToListAsync());
            Assert.False(payment.IsFinal);
            Assert.NotNull(Assert.Single(await db.OutgoingMessages.AsNoTracking().ToListAsync()).StartedAtUtc);
            // The stopped instance still owns the payment until its claim expires, so the next one must wait for that.
            Assert.NotNull(Assert.Single(await db.OutgoingMetadata.AsNoTracking().ToListAsync()).ClaimToken);
        }

        fixture.Block = false;
        using var second = fixture.Host();
        using var secondClient = second.CreateClient();
        await EventuallyAsync(() => Task.FromResult(fixture.Callbacks.Count == 1));
        await Task.Delay(Settle);

        Assert.Equal(new[] { false, true }, fixture.PossibleDuplicateFlags.ToArray());
        var sent = fixture.PaymentSubmissions;
        Assert.Equal(2, sent.Length);
        Assert.Equal(sent[0], sent[1]);
        await using var final = fixture.Database.Context();
        Assert.Equal(TransactionStatus.Accepted, Assert.Single(await final.Payments.AsNoTracking().ToListAsync()).CurrentStatus);
    }

    [Fact]
    public async Task Work_accepted_after_one_instance_stops_is_finished_by_the_instance_still_running()
    {
        await using var fixture = await CreateAsync();
        using var first = fixture.Host(new() { ["Payments:Outgoing:Transport:IpsVersion"] = "1" });
        using var second = fixture.Host(new() { ["Payments:Outgoing:Transport:IpsVersion"] = "2" });
        using var firstClient = first.CreateClient();
        using var secondClient = second.CreateClient();
        var runtime = first.Services.GetRequiredService<OutgoingRuntime>();
        await runtime.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(runtime.TryStart(Guid.NewGuid()));

        for (var number = 0; number < 5; number++)
        {
            await SeedAsync(fixture, $"handover-{number}");
        }

        await EventuallyAsync(() => Task.FromResult(fixture.Callbacks.Count == 5));
        await Task.Delay(Settle);
        Assert.Equal(5, fixture.Submissions.Count);
        Assert.Equal(5, fixture.Callbacks.Count);
        Assert.All(fixture.IpsVersions, version => Assert.Equal("2", version));
    }

    [Fact]
    public async Task A_payment_accepted_while_an_instance_drains_is_stored_not_sent_there_answers_504_and_is_completed_once_by_another_instance()
    {
        await using var fixture = await CreateAsync();
        fixture.Configuration["Payments:Outgoing:Execution:ShutdownBudget"] = "00:00:08";
        fixture.Block = true;
        using var first = fixture.Host();
        using var client = first.CreateClient();
        var inFlight = client.PostAsJsonAsync(Send, Pain002Request("in-flight"));
        await fixture.FirstSend.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var stopping = first.Services.GetRequiredService<OutgoingRuntime>().StopAsync(default);

        var duringDrain = client.PostAsJsonAsync(Send, Pain002Request("during-drain"));
        await EventuallyAsync(async () =>
        {
            await using var read = fixture.Database.Context();
            return await read.Payments.AsNoTracking().CountAsync() == 2;
        });
        Assert.Single(fixture.Submissions);
        // The blocked send is released inside its request timeout, so its outcome is known.
        fixture.Release.TrySetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(15));
        using var inFlightResponse = await inFlight;
        using var accepted = await duringDrain;

        Assert.Equal(HttpStatusCode.OK, inFlightResponse.StatusCode);
        Assert.Equal(HttpStatusCode.GatewayTimeout, accepted.StatusCode);
        using (var body = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync()))
        {
            Assert.Equal("during-drain", body.RootElement.GetProperty("clientReference").GetString());
            Assert.NotEqual("Accepted", body.RootElement.GetProperty("status").GetString());
        }

        Assert.Single(fixture.Submissions);
        await using (var db = fixture.Database.Context())
        {
            var unfinished = Assert.Single((await db.Payments.AsNoTracking().ToListAsync()).Where(payment => !payment.IsFinal));
            Assert.Equal("during-drain", unfinished.ClientReference);
        }

        fixture.Block = false;
        using var second = fixture.Host();
        using var secondClient = second.CreateClient();
        await EventuallyAsync(() => Task.FromResult(fixture.Callbacks.Count == 2));
        await Task.Delay(Settle);

        Assert.Equal(2, fixture.Submissions.Count);
        Assert.Equal(2, fixture.Callbacks.Count);
        await using var final = fixture.Database.Context();
        Assert.All(await final.Payments.AsNoTracking().ToListAsync(), payment => Assert.Equal(TransactionStatus.Accepted, payment.CurrentStatus));
    }

    [Fact]
    public async Task The_host_shutdown_timeout_covers_the_drain_budget_and_the_longest_evidence_persistence()
    {
        await using var fixture = await CreateAsync();
        var drain = TimeSpan.FromSeconds(20);
        fixture.Configuration["Payments:Outgoing:Execution:ShutdownBudget"] = drain.ToString();
        using var host = fixture.Host();
        using var client = host.CreateClient();
        var services = host.Services;
        var persistence = new[]
        {
            services.GetRequiredService<Pacs008Options>().PersistenceBudget,
            services.GetRequiredService<StatusDeliveryOptions>().PersistenceBudget,
            services.GetRequiredService<InvestigationOptions>().PersistenceBudget
        }.Max();

        var hostOptions = services.GetRequiredService<IOptions<HostOptions>>().Value;

        Assert.True(hostOptions.ShutdownTimeout >= drain + persistence, $"The host would stop after {hostOptions.ShutdownTimeout}, before the drain and the evidence persistence.");
        Assert.True(hostOptions.ServicesStopConcurrently);
    }

    // Budgets keep the validated ordering (request, HTTP wait, attempt and persistence inside ownership) with room for the simulator, which signs each reply with Java.
    private static async Task<OutgoingHostFixture> CreateAsync()
    {
        var fixture = await OutgoingHostFixture.CreateAsync();
        fixture.Configuration["Payments:Outgoing:Transport:Ips:ConnectTimeout"] = "00:00:00.500";
        fixture.Configuration["Payments:Outgoing:Transport:Ips:RequestTimeout"] = "00:00:05";
        fixture.Configuration["Payments:Outgoing:Execution:HttpWait"] = "00:00:06";
        fixture.Configuration["Payments:Outgoing:Execution:AttemptBudget"] = "00:00:07";
        fixture.Configuration["Payments:Outgoing:Pacs008:Ownership"] = "00:00:10";
        return fixture;
    }

    private static string? ClientReferenceOf(string callback)
    {
        using var document = JsonDocument.Parse(callback);
        return document.RootElement.GetProperty("clientReference").GetString();
    }

    // An accepted payment that no instance has started yet, as left by an instance that stopped or crashed after intake.
    private static async Task SeedAsync(OutgoingHostFixture fixture, string reference)
    {
        await using var session = fixture.Database.Session();
        var request = Pain002Fixture.Request(reference);
        var intake = new Pain002Intake(session.Payments, new(session.Payments, session.Unit, TimeProvider.System),
            Pacs008Fixture.Policy, new("NBGEGE22"), TimeProvider.System);
        var result = await intake.AcceptAsync(request, JsonSerializer.Serialize(request), default);
        Assert.True(result.Intake!.Created);
    }
}
