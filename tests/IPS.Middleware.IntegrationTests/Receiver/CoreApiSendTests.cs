using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using IPS.Middleware.Api.Payments;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs004;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pacs009;
using IPS.Middleware.IntegrationTests.Transport;
using IPS.MiidleWear.Contracts.Http;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.CoreAPI;
using IPS.MiidleWear.CoreAPI.Sending;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Receiver;

// CoreAPI plays the sending core system: what it generates must pass the gateway's own mapping and validation.
public sealed class CoreApiSendTests
{
    // The Uat gateway: Bank:BicCode ITSBGE22 and GEL up to 1000.
    private static readonly Pacs008Policy Policy = new(new BankIdentity("ITSBGE22", "GEATSAAA", "TRESGE22"), [new PaymentCurrency("GEL", true, 0.01m, 1000m)]);

    private static readonly PaymentGenerator Generator = new(Options.Create(new GatewayOptions()));

    [Fact]
    public void Every_generated_transfer_passes_the_gateway_validation()
    {
        for (var run = 0; run < 100; run++)
        {
            Assert.Empty(ValidatedPacs008.Validate(Pacs008RequestMapping.Map(Generator.Pacs008(null, null, null)), Policy).Errors);
            Assert.Empty(ValidatedPacs009.Validate(Pacs009RequestMapping.Map(Generator.Pacs009(null, null, null, null)), Policy).Errors);
            Assert.Empty(ValidatedPacs004.Validate(Pacs004RequestMapping.Map(Generator.Pacs004(null, null, null, null)), Policy).Errors);
        }
    }

    [Fact]
    public void The_query_values_replace_the_random_ones()
    {
        var payment = Generator.Pacs008("gel", "tbcbge22", 12.5m);

        Assert.Equal(("GEL", "TBCBGE22", 12.5m), (payment.Currency, payment.Creditor!.ParticipantBic, payment.Amount));
    }

    [Fact]
    public async Task A_send_posts_to_the_gateway_route_and_returns_its_answer_unchanged()
    {
        var received = new ConcurrentQueue<(string Path, string Body)>();
        await using var gateway = await HttpSimulator.StartAsync(async context =>
        {
            using var reader = new StreamReader(context.Request.Body);
            received.Enqueue((context.Request.Path, await reader.ReadToEndAsync()));
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            await context.Response.WriteAsJsonAsync(new { status = "Sending" });
        });
        await using var app = SampleApp.Create([], builder =>
        {
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Gateway:BaseUrl"] = gateway.Url });
        });
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single() + "/") };

        using var response = await client.PostAsync("core/send/pacs008?amount=10", null);

        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(504, result.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal("Sending", result.RootElement.GetProperty("response").GetProperty("status").GetString());
        var (path, body) = Assert.Single(received);
        Assert.Equal(Pacs008RestApiRoutes.Send, path);
        Assert.Equal(10m, JsonSerializer.Deserialize<Pacs008InstantPaymentRequestDto>(body, IpsJson.SerializerOptions)!.Amount);
    }
}
