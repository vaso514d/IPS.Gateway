using System.Net;
using System.Net.Http.Json;
using IPS.MiidleWear.Contracts.Transactions;
using Xunit;

namespace IPS.Middleware.AspireTests;

// The same stack with the API built from its Dockerfile and run as a container.
public sealed class ContainerTests
{
    [DockerFact]
    public async Task The_service_runs_from_its_image_reports_ready_and_sends_one_payment_with_its_callback()
    {
        await using var stack = await Stack.StartAsync(container: true);
        using var api = stack.Api();
        var reference = Requests.Reference("container");

        using var ready = await api.GetAsync("/health/ready");
        using var response = await api.PostAsJsonAsync(Requests.Pain002Send, Requests.Pain002(reference));

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(TransactionStatus.Accepted, (await response.Content.ReadFromJsonAsync<TransactionStatusDto>())!.Status);
        var received = await stack.WaitForSettledAsync(r => r.Callbacks.Count == 1 && r.Messages.Count == 1);
        Assert.Single(received.Messages);
    }
}
