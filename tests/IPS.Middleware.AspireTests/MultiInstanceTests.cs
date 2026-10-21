using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IPS.MiidleWear.Contracts.Transactions;
using Xunit;

namespace IPS.Middleware.AspireTests;

// The 011 proof against containers: instances that share one database never send or report a payment twice.
public sealed class MultiInstanceTests
{
    [DockerFact]
    public async Task Two_instances_share_the_work_so_every_payment_is_sent_once_and_reported_once_with_both_taking_part()
    {
        await using var stack = await Stack.StartAsync(instances: 2);
        using var first = stack.Api(1);
        using var second = stack.Api(2);
        const int payments = 40;
        var references = Enumerable.Range(0, payments).Select(number => Requests.Reference($"race{number}")).ToArray();

        // All payments are accepted through the first instance, whose admission limit leaves most of them waiting; the recovery
        // sweeps of both instances then claim them.
        var responses = await Task.WhenAll(references.Select(reference => first.PostAsJsonAsync(Requests.Pain002Send, Requests.Pain002(reference))));
        var received = await stack.WaitForSettledAsync(r => r.Callbacks.Count == payments && r.Messages.Count == payments, TimeSpan.FromSeconds(120));

        Assert.All(responses, response => Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.GatewayTimeout));
        Assert.Equal(payments, received.Messages.Count);
        Assert.Equal(payments, received.Messages.Select(message => message.Xml).Distinct().Count());
        Assert.All(received.Messages, message => Assert.False(message.PossibleDuplicate));
        Assert.Equal(new[] { "1", "2" }, received.Messages.Select(message => message.Version ?? "").Distinct().Order().ToArray());
        Assert.Equal(payments, received.Callbacks.Count);
        Assert.Equal(references.Order(), received.Callbacks.Select(ClientReferenceOf).Order());
        foreach (var reference in references)
        {
            using var status = await second.GetAsync($"/api/ips/transactions/status?messageKind=Pain002&clientReference={reference}");
            Assert.Equal(TransactionStatus.Accepted, (await status.Content.ReadFromJsonAsync<TransactionStatusDto>())!.Status);
        }
    }

    private static string? ClientReferenceOf(string callback)
    {
        using var document = JsonDocument.Parse(callback);
        return document.RootElement.GetProperty("clientReference").GetString();
    }
}
