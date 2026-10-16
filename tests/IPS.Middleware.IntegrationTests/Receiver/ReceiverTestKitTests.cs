using System.Collections.Concurrent;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.MiidleWear.Contracts.Http;
using IPS.MiidleWear.CoreReceiver.Sample;
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
        Assert.Equal(7 * 3 + 1 + 2 + 2, result.Checks.Count);
    }

    [Fact]
    public async Task The_sample_answers_pdng_to_the_status_query_while_posting_and_the_result_afterwards()
    {
        await using var sample = await SampleHost.StartAsync(TimeSpan.FromSeconds(1));
        using var body = new StringContent("""{"endToEndId":"E2E-SLOW","amount":10}""", System.Text.Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/ips/pacs008/receive") { Content = body };
        request.Headers.Add(IpsHttpHeaders.IdempotencyKey, "E2E-SLOW");

        var posting = sample.Client.SendAsync(request);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        var during = await sample.Client.GetStringAsync("api/ips/payments/status?messageKind=Pacs008&reference=E2E-SLOW");
        using var answer = await posting;
        var after = await sample.Client.GetStringAsync("api/ips/payments/status?messageKind=Pacs008&reference=E2E-SLOW");

        Assert.Contains("\"Status\":\"PDNG\"", during, StringComparison.Ordinal);
        Assert.Contains("\"Status\":\"ACCP\"", after, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> Faults() => new()
    {
        { "204-for-unknown", "the payment-status query of a message never sent answers 404" },
        { "new-answer-on-redelivery", "pacs.008: a redelivery gets the first answer" },
        { "wrong-end-to-end-id", "pacs.009: the answer is final" },
        { "status-enum-rejected", "the status callback is accepted" },
        { "duplicate-member", "pacs.004: the answer is final" },
        { "message-id-on-pacs008", "pacs.008: the answer is final" }
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

    public static TheoryData<int, string> Pacs008Answers() => new()
    {
        { 200, """{"Status":"ACCP"}""" },
        { 200, """{"status":"rjct","ReasonCode":"AC04"}""" },
        { 200, """{"Status":"ACCP","EndToEndId":"E2E-008"}""" },
        { 200, """{"Status":"ACCP","EndToEndId":"OTHER"}""" },
        { 200, """{"Status":"ACCP","Id":"ANY-MESSAGE-ID"}""" },
        { 200, """{"Status":"ACCP","TxId":"ANY-TX-ID"}""" },
        { 200, """{"Status":"PDNG"}""" },
        { 200, """{"Status":"ACCP","Status":"ACCP"}""" },
        { 200, """{"Status":"ACCP","status":"ACCP"}""" },
        { 200, """{"Status":"ACCP","Extra":1,"extra":2}""" },
        { 200, """{"Status":"ACCP","ProcessedAtUtc":"not a date"}""" },
        { 200, """{"Status":"ACCP","InternalErrorCode":"seven"}""" },
        { 200, """{"Status":"ACCP","ProcessedAtUtc":"2026-10-16T10:00:00+04:00","CoreReference":"POST-1"}""" },
        { 200, "not json" },
        { 204, "" },
        { 500, """{"Status":"ACCP"}""" }
    };

    [Theory]
    [MemberData(nameof(Pacs008Answers))]
    public void The_kit_and_the_gateway_agree_on_a_pacs008_answer(int status, string body)
    {
        var gateway = new IncomingCoreReplyInterpreter().Interpret(Completion(status, body),
            new IncomingPacs008Reference("BAH-008", "MSG-008", "E2E-008", "TX-008", null, null, null, null, null, null));

        var kit = AnswerReader.Read(status, body, new ExpectedEcho("E2E-008", null, null, AbsentWhenUnknown: true));

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
                case "new-answer-on-redelivery" when path.EndsWith("/pacs008/receive", StringComparison.Ordinal) && _deliveries.AddOrUpdate(key, 1, (_, count) => count + 1) > 1:
                    await Json(context, """{"Status":"RJCT","ReasonCode":"AM04"}""");
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
                case "message-id-on-pacs008" when path.EndsWith("/pacs008/receive", StringComparison.Ordinal):
                    await Json(context, """{"Status":"ACCP","Id":"CORE-MESSAGE-1"}""");
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
