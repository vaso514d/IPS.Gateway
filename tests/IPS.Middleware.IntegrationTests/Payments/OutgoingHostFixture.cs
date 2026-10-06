using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Xml.Linq;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.IntegrationTests.Transactions;
using IPS.Middleware.IntegrationTests.Transport;
using IPS.MiidleWear.Contracts.Camt029;
using IPS.MiidleWear.Contracts.Camt056;
using IPS.MiidleWear.Contracts.Pacs004;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace IPS.Middleware.IntegrationTests.Payments;

internal sealed class OutgoingHostFixture : IAsyncDisposable
{
    private int _unresolvedReplies;
    private int _lostReplies;

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
    public string? InvestigationAnswer { get; set; }
    public DateTimeOffset? FirstSendAtUtc { get; private set; }
    public DateTimeOffset? FirstInvestigationAtUtc { get; private set; }

    // pacs.008 submissions only; the runtime may also send pacs.028 investigations.
    public string[] PaymentSubmissions => Submissions.Where(xml => !IpsReplies.IsInvestigation(xml)).ToArray();

    // Whether each payment submission carried the possible-duplicate header, in arrival order.
    public ConcurrentQueue<bool> PossibleDuplicateFlags { get; } = new();

    // The next payment submissions never get a reply: IPS may have processed them, but the connection is dropped.
    public void LoseNextReplies(int submissions) => _lostReplies = submissions;

    // The next payment submissions receive an unresolved reply.
    public void AnswerUnresolved(int submissions) => _unresolvedReplies = submissions;
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

    public static Camt029ResolutionOfInvestigationDto Camt029Request(string reference = "outgoing29") => new()
    {
        ClientReference = reference,
        Id = "HOST-ANS-" + reference,
        CancellationStatusId = "HOST-CST-" + reference,
        OriginalMessageId = "RCL-MSG-HOST",
        OriginalEndToEndId = "ORIG-E2E-HOST",
        OriginalTransactionId = "ORIG-TX-HOST",
        ReasonCode = "CUST",
        OriginalTransaction = new()
        {
            Currency = "GEL",
            Amount = 10m,
            SettlementDate = new DateOnly(2026, 10, 3),
            Debtor = new() { Name = "Original Payer", Account = "GE29NB0000000101904917" },
            DebtorAgent = new() { Bicfi = "TBCBGE22" },
            CreditorAgent = new() { Bicfi = "BAGAGE22" },
            Creditor = new() { Name = "Original Payee", Account = "GE95TB0000000123456789" }
        }
    };

    public static Camt056RecallRequestDto Camt056Request(string reference = "outgoing56") => new()
    {
        ClientReference = reference,
        Id = "HOST-RCL-" + reference,
        RecallId = "HOST-CXL-" + reference,
        OriginalMessageId = "ORIG-MSG-HOST",
        OriginalEndToEndId = "ORIG-E2E-HOST",
        OriginalTransactionId = "ORIG-TX-HOST",
        OriginalCurrency = "GEL",
        OriginalAmount = 10m,
        OriginalSettlementDate = new DateOnly(2026, 10, 3),
        ReasonCode = "DUPL",
        OriginalTransaction = new()
        {
            SettlementDate = new DateOnly(2026, 10, 3),
            Debtor = new() { Name = "Original Payer", Account = "GE29NB0000000101904917" },
            DebtorAgent = new() { Bicfi = "BAGAGE22" },
            CreditorAgent = new() { Bicfi = "TBCBGE22" },
            Creditor = new() { Name = "Original Payee", Account = "GE95TB0000000123456789" }
        }
    };

    public static Pacs004PaymentReturnRequestDto Pacs004Request(string reference = "outgoing4") => new()
    {
        ClientReference = reference,
        Id = "HOST-RTR-" + reference,
        Amount = 10m,
        Currency = "GEL",
        ValueDate = new DateOnly(2026, 10, 4),
        InstructedAgent = "TBCBGE22",
        Debtor = new() { Name = "Original Payer", Account = "GE95TB0000000123456789" },
        Creditor = new() { Name = "Original Payee", Account = "GE29NB0000000101904917" },
        Original = new()
        {
            TransactionId = "ORIG-TX-HOST",
            EndToEndId = "ORIG-E2E-HOST",
            ValueDate = new DateOnly(2026, 10, 3)
        }
    };

    public static Pacs009PaymentRequestDto Pacs009Request(string reference = "outgoing9") => new()
    {
        ClientReference = reference,
        Id = "HOST-" + reference,
        DebtorAgent = new() { Bicfi = "BAGAGE22" },
        CreditorAgent = new() { Bicfi = "TBCBGE22" },
        EndToEndId = "E2E-HOST-009",
        ValueDate = new DateOnly(2026, 10, 4),
        Currency = "GEL",
        Amount = 10m,
        Debtor = new() { Account = "GE29NB0000000101904917" },
        Creditor = new() { Account = "GE95TB0000000123456789" }
    };

    public static Pacs008InstantPaymentRequestDto Request(string reference = "outgoing")
    {
        var now = DateTimeOffset.UtcNow;
        return IncomingPacs008CoreMapping.ToContract(Pacs008Fixture.Request() with { CreationDateTime = now.AddMilliseconds(-500), AcceptanceDateTime = now }) with
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
        if (IpsReplies.IsInvestigation(xml))
        {
            await AnswerInvestigationAsync(context, xml);
            return;
        }

        PossibleDuplicateFlags.Enqueue(context.Request.Headers.ContainsKey("X-MONTRAN-RTP-PossibleDuplicate"));
        FirstSendAtUtc ??= DateTimeOffset.UtcNow;
        FirstSend.TrySetResult();
        if (Block)
        {
            await Release.Task.WaitAsync(context.RequestAborted);
        }

        if (LoseReply || Interlocked.Decrement(ref _lostReplies) >= 0)
        {
            context.Abort();
            return;
        }

        if (Unresolved || Interlocked.Decrement(ref _unresolvedReplies) >= 0)
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsync("raw upstream failure");
            return;
        }

        var document = XDocument.Parse(xml);
        string Value(params string[] names) => document.Descendants().First(e => names.Contains(e.Name.LocalName)).Value;
        var status = Reject ? "RJCT" : "ACCP";
        var reply = new IpsReplies.Reply
        {
            MessageId = Value("BizMsgIdr"),
            TransactionId = Value("OrgnlTxId", "TxId"),
            EndToEndId = Value("OrgnlEndToEndId", "EndToEndId"),
            OriginalMessageName = Value("MsgDefIdr"),
            GroupStatus = status,
            TransactionStatus = status,
            ReasonCode = Reject ? "AC01" : null
        };
        context.Response.Headers["X-MONTRAN-IPS-ReqSts"] = Reject ? "RJCT/1009" : "ACCP";
        await context.Response.WriteAsync((await IpsReplies.SignAsync(Certificates.Client, IpsReplies.Unsigned(reply)))[0]);
    }

    // Without a configured answer the investigation stays unresolved.
    private async Task AnswerInvestigationAsync(HttpContext context, string xml)
    {
        FirstInvestigationAtUtc ??= DateTimeOffset.UtcNow;
        if (InvestigationAnswer is not { } answer)
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsync("no investigation answer");
            return;
        }

        var reply = await IpsReplies.AnswerInvestigationAsync(Certificates.Client, xml, answer);
        foreach (var header in reply.Headers)
        {
            context.Response.Headers[header.Name] = header.Value;
        }

        await context.Response.WriteAsync(reply.Body);
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
