using System.Collections.Concurrent;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.MiidleWear.Contracts.Http;
using IPS.MiidleWear.CoreAPI;
using IPS.MiidleWear.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Receiver;

// The receiver test kit (018) against the sample receiver, against receivers with one fault each, and against the
// gateway's own interpreter: the kit passes only what the gateway accepts.
public sealed class ReceiverTestKitTests
{
    [Fact]
    public async Task The_sample_receiver_passes_every_check()
    {
        await using var sample = await SampleHost.StartAsync();

        var result = await IpsReceiverConformance.RunAsync(sample.Client);

        Assert.True(result.Passed, result.ToString());
        Assert.Equal(7 * 3 + 2 + 2 + 2, result.Checks.Count);
    }

    [Fact]
    public async Task The_sample_answers_pending_to_the_status_query_while_posting_and_the_result_afterwards()
    {
        await using var sample = await SampleHost.StartAsync(TimeSpan.FromSeconds(1));
        using var body = new StringContent("""{"transactionId":"TX-SLOW","operationType":"INSTANT_PAYMENT","data":{"endToEndId":"E2E-SLOW","amount":10}}""",
            System.Text.Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "stp/incoming-payments") { Content = body };
        request.Headers.Add(IpsHttpHeaders.IdempotencyKey, "TX-SLOW");

        var posting = sample.Client.SendAsync(request);
        // Under load the posting may not have started yet; the query answers 404 until it has.
        var during = "";
        for (var attempt = 0; attempt < 50 && !during.Contains("PENDING", StringComparison.Ordinal); attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20));
            using var query = await sample.Client.GetAsync("stp/incoming-payments/TX-SLOW");
            during = query.IsSuccessStatusCode ? await query.Content.ReadAsStringAsync() : "";
        }

        using var answer = await posting;
        var after = await sample.Client.GetStringAsync("stp/incoming-payments/TX-SLOW");

        Assert.Contains("\"status\":\"PENDING\"", during, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"ACCEPTED\"", after, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> Faults() => new()
    {
        { "204-for-unknown", "the payment-status query of a message never sent answers 404" },
        { "new-answer-on-redelivery", "pacs.008: a redelivery gets the first answer" },
        { "wrong-end-to-end-id", "pacs.009: the answer is final" },
        { "status-enum-rejected", "the status callback is accepted" },
        { "duplicate-member", "pacs.004: the answer is final" },
        { "other-transaction-id-on-pacs008", "pacs.008: the answer is final" }
    };

    [Theory]
    [MemberData(nameof(Faults))]
    public async Task A_receiver_with_one_fault_fails_the_check_that_names_it(string fault, string check)
    {
        await using var sample = await SampleHost.StartAsync(fault: fault);

        var result = await IpsReceiverConformance.RunAsync(sample.Client);

        var failed = Assert.Single(result.Failures, failure => failure.Name == check);
        Assert.False(string.IsNullOrWhiteSpace(failed.Reason));
        Assert.Throws<IpsReceiverConformanceException>(result.EnsurePassed);
    }

    private const string PaymentId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    public static TheoryData<int, string> Pacs008Answers() => new()
    {
        { 200, """{"status":"ACCEPTED"}""" },
        { 200, """{"Status":"rejected","reasonCode":"AC04","reasonDescription":"Account closed"}""" },
        { 200, $$"""{"status":"ACCEPTED","transactionId":"{{PaymentId}}"}""" },
        { 200, """{"status":"ACCEPTED","transactionId":"OTHER"}""" },
        { 200, """{"status":"PENDING"}""" },
        { 200, """{"status":"ACCP"}""" },
        { 200, """{"status":"ACCEPTED","status":"ACCEPTED"}""" },
        { 200, """{"status":"ACCEPTED","Status":"ACCEPTED"}""" },
        { 200, """{"status":"ACCEPTED","Extra":1,"extra":2}""" },
        { 200, """{"status":"ACCEPTED","reasonDescription":5}""" },
        { 200, """{"status":"ACCEPTED","processedAtUtc":"not a date"}""" },
        { 200, """{"status":"ACCEPTED","coreReference":"POST-1"}""" },
        { 200, "not json" },
        { 204, "" },
        { 500, """{"status":"ACCEPTED"}""" }
    };

    [Theory]
    [MemberData(nameof(Pacs008Answers))]
    public void The_kit_and_the_gateway_agree_on_a_pacs008_answer(int status, string body)
    {
        var gateway = new IncomingCoreReplyInterpreter().Interpret(Completion(status, body),
            IncomingPayment.Register(Guid.Parse(PaymentId), "TESTGE22", "E2E-008", DateTimeOffset.UtcNow));

        var kit = AnswerReader.ReadIncomingPayment(new Exchange(status, body), PaymentId);

        Assert.Equal(gateway.Status != CoreOutcome.Unknown, kit.Final);
    }

    public static TheoryData<int, string> TransferAnswers() => new()
    {
        { 200, """{"Status":"ACCP"}""" },
        { 200, """{"Status":"ACCP","Id":"MSG-IN-1","EndToEndId":"E2E-IN-1","TxId":"TX-IN-1"}""" },
        { 200, """{"Status":"RJCT","Id":"OTHER"}""" },
        { 200, """{"Status":"ACCP","TxId":"OTHER"}""" },
        { 200, """{"Status":"PDNG","Id":"MSG-IN-1"}""" },
        { 404, "" }
    };

    [Theory]
    [MemberData(nameof(TransferAnswers))]
    public void The_kit_and_the_gateway_agree_on_a_transfer_answer(int status, string body)
    {
        var transfer = new IncomingPacs009(
            "MSG-IN-1", "E2E-IN-1", null, "TX-IN-1", null, null, new DateOnly(2026, 10, 4), "GEL", 100.5m,
            new IncomingAgent("TBCBGE22", null), new IncomingAgent("TESTGE22", null), null, null, null, null, null);
        var gateway = ((IIncomingTransferReplyInterpreter)new IncomingCoreReplyInterpreter()).Interpret(Completion(status, body), transfer);

        var kit = AnswerReader.Read(status, body, new ExpectedEcho("E2E-IN-1", "MSG-IN-1", "TX-IN-1", AbsentWhenUnknown: false));

        Assert.Equal(gateway.Status != CoreOutcome.Unknown, kit.Final);
    }

    private static CoreCallCompletion Completion(int status, string body) =>
        new(new CoreResponse(status, body, []), null, DateTimeOffset.UtcNow);

    private sealed class SampleHost(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Client { get; } = new() { BaseAddress = new Uri(app.Urls.Single() + "/") };

        public static async Task<SampleHost> StartAsync(TimeSpan? postingDelay = null, string? fault = null)
        {
            var app = SampleApp.Create([], builder =>
            {
                builder.Logging.ClearProviders();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{SampleOptions.Section}:{nameof(SampleOptions.PostingDelay)}"] = (postingDelay ?? TimeSpan.Zero).ToString()
                });
                if (fault is not null)
                {
                    builder.Services.AddSingleton<IStartupFilter>(new FaultFilter(fault));
                }
            });
            await app.StartAsync();
            return new(app);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    // Puts one fault in front of the sample receiver, so every other check still passes.
    private sealed class FaultFilter(string fault) : IStartupFilter
    {
        private readonly ConcurrentDictionary<string, int> _deliveries = new(StringComparer.Ordinal);

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, call) =>
            {
                if (!await FaultAsync(context))
                {
                    await call(context);
                }
            });
            next(app);
        };

        private async Task<bool> FaultAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "";
            var key = context.Request.Headers[IpsHttpHeaders.IdempotencyKey].ToString();
            switch (fault)
            {
                case "204-for-unknown" when context.Request.Query["reference"].ToString().StartsWith("KIT-NEVER", StringComparison.Ordinal):
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return true;
                case "new-answer-on-redelivery" when path.EndsWith("/stp/incoming-payments", StringComparison.Ordinal) && _deliveries.AddOrUpdate(key, 1, (_, count) => count + 1) > 1:
                    await Json(context, """{"status":"REJECTED","reasonCode":"AM04"}""");
                    return true;
                case "wrong-end-to-end-id" when path.EndsWith("/pacs009/receive", StringComparison.Ordinal):
                    await Json(context, """{"Status":"ACCP","EndToEndId":"WRONG"}""");
                    return true;
                case "status-enum-rejected" when path.EndsWith("/transactions/status/receive", StringComparison.Ordinal) && !key.StartsWith("in:", StringComparison.Ordinal):
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("The JSON value could not be converted to TransactionStatus.");
                    return true;
                case "duplicate-member" when path.EndsWith("/pacs004/receive", StringComparison.Ordinal):
                    await Json(context, """{"Status":"ACCP","Status":"ACCP"}""");
                    return true;
                case "other-transaction-id-on-pacs008" when path.EndsWith("/stp/incoming-payments", StringComparison.Ordinal):
                    await Json(context, """{"status":"ACCEPTED","transactionId":"OTHER"}""");
                    return true;
                default:
                    return false;
            }
        }

        private static Task Json(HttpContext context, string body)
        {
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync(body);
        }
    }
}
