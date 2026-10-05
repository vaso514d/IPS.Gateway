using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Xml.Linq;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.IntegrationTests.Transactions;
using IPS.Middleware.IntegrationTests.Transport;
using IPS.MiidleWear.Contracts.Pacs008;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace IPS.Middleware.IntegrationTests.Payments;

internal sealed class OutgoingHostFixture : IAsyncDisposable
{
    public SqlTestDatabase Database { get; private set; } = null!;
    public TransportCertificates Certificates { get; } = new();
    public HttpSimulator Server { get; private set; } = null!;
    public ConcurrentQueue<string> Submissions { get; } = new();
    public ConcurrentQueue<string> Callbacks { get; } = new();
    public TaskCompletionSource FirstSend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Block { get; set; }
    public bool Reject { get; set; }
    public bool LoseReply { get; set; }
    public bool Unresolved { get; set; }
    public Dictionary<string, string?> Configuration { get; private set; } = null!;

    public static async Task<OutgoingHostFixture> CreateAsync()
    {
        var fixture = new OutgoingHostFixture();
        fixture.Database = await SqlTestDatabase.CreateAsync();
        fixture.Server = await HttpSimulator.StartAsync(fixture.RespondAsync);
        await using var db = fixture.Database.Context();
        fixture.Configuration = new()
        {
            ["ConnectionStrings:Middleware"] = db.Database.GetConnectionString(),
            ["Payments:Outgoing:Transport:Enabled"] = "true",
            ["Payments:Outgoing:Transport:ParticipantBic"] = "BAGAGE22",
            ["Payments:Outgoing:Transport:Ips:BaseUrl"] = fixture.Server.Url,
            ["Payments:Outgoing:Transport:Cbs:BaseUrl"] = fixture.Server.Url,
            ["Payments:Outgoing:Transport:Ips:RequestTimeout"] = "00:00:10",
            ["Payments:Outgoing:Transport:IpsSignatureTrust:0:Path"] = fixture.Certificates.SignatureTrust.Path,
            ["Payments:Signing:AllowUnsignedInDevelopment"] = "true",
            ["Payments:Outgoing:Execution:Enabled"] = "true",
            ["Payments:Outgoing:Execution:HttpWait"] = "00:00:12",
            ["Payments:Outgoing:Execution:AttemptBudget"] = "00:00:15",
            ["Payments:Outgoing:Execution:ShutdownBudget"] = "00:00:01",
            ["Payments:Outgoing:Execution:DiscoveryInterval"] = "00:00:00.050",
            ["Payments:Outgoing:StatusDelivery:DiscoveryInterval"] = "00:00:00.050",
            ["Payments:Outgoing:Policy:Currencies:0:Code"] = "GEL",
            ["Payments:Outgoing:Protocol:IpsBic"] = "NBGEGE22"
        };
        return fixture;
    }

    public WebApplicationFactory<Program> Host() => new ConfiguredHost(Configuration);
    private sealed class ConfiguredHost(Dictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
    }

    public static Pacs008InstantPaymentRequestDto Request(string reference = "outgoing")
    {
        var now = DateTimeOffset.UtcNow;
        return IncomingPacs008CoreMapping.ToContract(new Application.Payments.Pacs008.Pacs008Request(Pacs008Fixture.Request()) { CreationDateTime = now.AddMilliseconds(-500), AcceptanceDateTime = now }) with
        {
            ClientReference = reference
        };
    }

    private async Task RespondAsync(HttpContext context)
    {
        if (context.Request.Path == "/api/ips/transactions/status/receive")
        {
            Callbacks.Enqueue(await new StreamReader(context.Request.Body).ReadToEndAsync());
            context.Response.StatusCode = 204;
            return;
        }

        AssertPath(context);
        var xml = await new StreamReader(context.Request.Body).ReadToEndAsync();
        Submissions.Enqueue(xml);
        FirstSend.TrySetResult();
        if (Block)
        {
            await Release.Task.WaitAsync(context.RequestAborted);
        }

        if (LoseReply)
        {
            context.Abort();
            return;
        }

        if (Unresolved)
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsync("raw upstream failure");
            return;
        }

        var document = XDocument.Parse(xml);
        string Value(string name) => document.Descendants().First(e => e.Name.LocalName == name).Value;
        var status = Reject ? "RJCT" : "ACCP";
        var reply = new IpsReplies.Reply
        {
            MessageId = Value("BizMsgIdr"),
            TransactionId = Value("TxId"),
            EndToEndId = Value("EndToEndId"),
            GroupStatus = status,
            TransactionStatus = status,
            ReasonCode = Reject ? "AC01" : null
        };
        context.Response.Headers["X-MONTRAN-IPS-ReqSts"] = Reject ? "RJCT/1009" : "ACCP";
        await context.Response.WriteAsync((await IpsReplies.SignAsync(Certificates.Client, IpsReplies.Unsigned(reply)))[0]);
    }

    private static void AssertPath(HttpContext context)
    {
        if (context.Request.Method != "POST" || context.Request.Path != "/Message")
        {
            throw new InvalidOperationException("Unexpected simulator operation.");
        }
    }

    public static async Task EventuallyAsync(Func<Task<bool>> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!await ready())
        {
            await Task.Delay(25, timeout.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Release.TrySetResult();
        await Server.DisposeAsync();
        Certificates.Dispose();
        await Database.DisposeAsync();
    }
}
