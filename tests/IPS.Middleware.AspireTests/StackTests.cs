using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IPS.MiidleWear.Contracts.Proxy;
using IPS.MiidleWear.Contracts.Transactions;
using Xunit;

namespace IPS.Middleware.AspireTests;

[Collection("Single instance stack")]
public sealed class StackTests(SingleInstanceStack shared)
{
    private Stack Stack => shared.Started;

    [DockerFact]
    public async Task The_stack_starts_and_the_service_reports_ready()
    {
        await Stack.ResetAsync();
        using var api = Stack.Api();

        using var ready = await api.GetAsync("/health/ready");
        using var live = await api.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [DockerFact]
    public async Task A_pain002_is_sent_to_ips_and_ends_accepted_with_one_callback_to_the_core()
    {
        await Stack.ResetAsync();
        using var api = Stack.Api();
        var reference = Requests.Reference("pain");

        using var response = await api.PostAsJsonAsync(Requests.Pain002Send, Requests.Pain002(reference));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = (await response.Content.ReadFromJsonAsync<TransactionStatusDto>())!;
        Assert.Equal(TransactionStatus.Accepted, status.Status);
        Assert.Equal(reference, status.ClientReference);
        var received = await Stack.WaitForSettledAsync(r => r.Callbacks.Count == 1 && r.Messages.Count == 1);
        Assert.Single(received.Messages);
        Assert.False(received.Messages[0].PossibleDuplicate);
        Assert.Contains(reference, received.Callbacks[0], StringComparison.Ordinal);
    }

    [DockerFact]
    public async Task A_pacs008_is_sent_its_signed_reply_is_verified_and_it_ends_accepted_with_one_callback()
    {
        await Stack.ResetAsync();
        using var api = Stack.Api();
        var reference = Requests.Reference("pacs");

        using var response = await api.PostAsJsonAsync(Requests.Pacs008Send, Requests.Pacs008(reference));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TransactionStatus.Accepted, (await response.Content.ReadFromJsonAsync<TransactionStatusDto>())!.Status);
        var received = await Stack.WaitForSettledAsync(r => r.Callbacks.Count == 1 && r.Messages.Count == 1);
        Assert.Single(received.Messages);
    }

    [DockerFact]
    public async Task A_rejecting_ips_ends_the_payment_rejected_and_still_reports_it_once()
    {
        await Stack.ResetAsync();
        await Stack.BehaveAsync(new { reject = true });
        using var api = Stack.Api();

        using var response = await api.PostAsJsonAsync(Requests.Pain002Send, Requests.Pain002(Requests.Reference("rejected")));

        Assert.Equal(TransactionStatus.Rejected, (await response.Content.ReadFromJsonAsync<TransactionStatusDto>())!.Status);
        var received = await Stack.WaitForSettledAsync(r => r.Callbacks.Count == 1 && r.Messages.Count == 1);
        Assert.Equal(TransactionStatus.Rejected, JsonSerializer.Deserialize<TransactionStatusDto>(received.Callbacks[0], JsonSerializerOptions.Web)!.Status);
    }

    [DockerFact]
    public async Task A_lost_reply_is_recovered_by_one_flagged_resend_of_the_same_bytes()
    {
        await Stack.ResetAsync();
        await Stack.BehaveAsync(new { loseNext = 1 });
        using var api = Stack.Api();
        var reference = Requests.Reference("lost");

        using var first = await api.PostAsJsonAsync(Requests.Pain002Send, Requests.Pain002(reference));

        Assert.True(first.StatusCode is HttpStatusCode.OK or HttpStatusCode.GatewayTimeout);
        var received = await Stack.WaitForSettledAsync(r => r.Callbacks.Count == 1 && r.Messages.Count == 2, TimeSpan.FromSeconds(90));
        Assert.Equal(new[] { false, true }, received.Messages.Select(message => message.PossibleDuplicate).ToArray());
        Assert.Equal(received.Messages[0].Xml, received.Messages[1].Xml);
        using var status = await api.GetAsync($"/api/ips/transactions/status?messageKind=Pain002&clientReference={reference}");
        Assert.Equal(TransactionStatus.Accepted, (await status.Content.ReadFromJsonAsync<TransactionStatusDto>())!.Status);
    }

    [DockerFact]
    public async Task Proxy_management_returns_the_proxys_accept_and_its_reject_for_each_operation()
    {
        await Stack.ResetAsync();
        using var api = Stack.Api();

        var accepted = new[]
        {
            await api.PostAsJsonAsync("/api/proxy/register", Requests.ProxyRegister()),
            await api.PostAsJsonAsync("/api/proxy/update", Requests.ProxyUpdate()),
            await api.PostAsJsonAsync("/api/proxy/remove", Requests.ProxyRemove())
        };
        await Stack.BehaveAsync(new { rejectProxy = true });
        using var rejected = await api.PostAsJsonAsync("/api/proxy/register", Requests.ProxyRegister());

        foreach (var response in accepted)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True((await response.Content.ReadFromJsonAsync<ProxyOperationResultDto>())!.Accepted);
            response.Dispose();
        }

        var reject = (await rejected.Content.ReadFromJsonAsync<ProxyOperationResultDto>())!;
        Assert.False(reject.Accepted);
        Assert.Equal("AM05", reject.ErrorCode);
        Assert.Equal(new[] { "register", "update", "remove", "register" }, (await Stack.ReceivedAsync()).ProxyCalls);
    }

    [DockerFact]
    public async Task Invalid_requests_are_400_and_never_reach_the_simulators()
    {
        await Stack.ResetAsync();
        using var api = Stack.Api();

        using var payment = await api.PostAsJsonAsync(Requests.Pain002Send, Requests.Pain002(Requests.Reference("bad")) with { ReasonCode = "TOOLONG" });
        using var proxy = await api.PostAsJsonAsync("/api/proxy/remove", Requests.ProxyRemove() with { AccountIdentifier = "not-an-iban" });

        Assert.Equal(HttpStatusCode.BadRequest, payment.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, proxy.StatusCode);
        var received = await Stack.ReceivedAsync();
        Assert.Empty(received.Messages);
        Assert.Empty(received.ProxyCalls);
        Assert.Empty(received.Callbacks);
    }
}
