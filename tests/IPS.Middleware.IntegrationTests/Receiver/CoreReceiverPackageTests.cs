using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Application.Payments.Camt029;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Recalls;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.IntegrationTests.Transport;
using IPS.MiidleWear.AspNetCore;
using IPS.MiidleWear.Contracts.Abstractions;
using IPS.MiidleWear.Contracts.Camt029;
using IPS.MiidleWear.Contracts.Camt055;
using IPS.MiidleWear.Contracts.Camt056;
using IPS.MiidleWear.Contracts.Http;
using IPS.MiidleWear.Contracts.Pacs004;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;
using IPS.MiidleWear.Contracts.Pain001;
using IPS.MiidleWear.Contracts.Routing;
using IPS.MiidleWear.Contracts.Transactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using DomainTransactionStatus = IPS.Middleware.Domain.Transactions.TransactionStatus;

namespace IPS.Middleware.IntegrationTests.Receiver;

// A receiver hosted with IPS.MiidleWear.AspNetCore (017b), called by the gateway's real CBS clients and read by its real
// interpreter: what the package serves is what the gateway accepts.
public sealed class CoreReceiverPackageTests
{
    private const string Participant = "TESTGE22";
    private const string Other = "TBCBGE22";

    public static TheoryData<string> Kinds() => new() { "pacs.009", "pacs.004", "pain.001", "camt.056", "camt.055", "camt.029" };

    [Fact]
    public async Task An_incoming_pacs008_reaches_the_receiver_with_its_key_and_an_accp_is_final()
    {
        await using var receiver = await ReceiverHost.StartAsync();
        using var certificates = new TransportCertificates();
        using var gateway = IncomingHttpClientTests.Services(IncomingHttpClientTests.Settings(receiver.Url, certificates));

        var response = await gateway.GetRequiredService<IIncomingCoreClient>().SubmitAsync(Participant, Pacs008(), default);

        var call = Assert.Single(receiver.Script.Calls);
        Assert.Equal((nameof(IClientPaymentReceiver.ReceivePacs008Async), "E2E-008"), (call.Operation, call.HeaderKey));
        Assert.Equal(call.HeaderKey, call.ContractKey);
        Assert.Equal(CoreOutcome.Accepted, Interpret(response).Status);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Every_incoming_transfer_reaches_its_operation_with_the_key_the_contract_derives(string kind)
    {
        await using var receiver = await ReceiverHost.StartAsync();
        using var certificates = new TransportCertificates();
        using var gateway = IncomingHttpClientTests.Services(IncomingHttpClientTests.Settings(receiver.Url, certificates));
        var transfer = Transfer(kind);

        var response = await gateway.GetRequiredService<IIncomingTransferCoreClient>().SubmitAsync(Participant, transfer, default);

        var call = Assert.Single(receiver.Script.Calls);
        Assert.Equal(transfer.Key, call.HeaderKey);
        Assert.Equal(call.HeaderKey, call.ContractKey);
        Assert.Equal(CoreOutcome.Accepted, InterpretTransfer(response, transfer).Status);
    }

    public static TheoryData<string, CoreOutcome> Answers() => new()
    {
        { "ACCP", CoreOutcome.Accepted },
        { "RJCT", CoreOutcome.Rejected },
        { "PDNG", CoreOutcome.Unknown },
        { "throw", CoreOutcome.Unknown },
        { "other-e2e", CoreOutcome.Unknown },
        { "message-id", CoreOutcome.Unknown }
    };

    [Theory]
    [MemberData(nameof(Answers))]
    public async Task Only_an_accp_or_an_rjct_with_matching_ids_is_final_and_a_receiver_failure_is_a_500(string answer, CoreOutcome outcome)
    {
        await using var receiver = await ReceiverHost.StartAsync();
        receiver.Script.Answer = (message, key) => answer switch
        {
            "RJCT" => Pacs008PaymentResultDto.Reject(null, key, null, "AC04"),
            "PDNG" => Pacs008PaymentResultDto.Pending(null, key, null),
            "throw" => throw new InvalidOperationException("Core database unavailable."),
            "other-e2e" => Pacs008PaymentResultDto.Accept(null, "ANOTHER", null),
            "message-id" => Pacs008PaymentResultDto.Accept("ANY-MESSAGE-ID", key, null),
            _ => Pacs008PaymentResultDto.Accept(null, key, null, "POSTING-1")
        };
        using var certificates = new TransportCertificates();
        using var gateway = IncomingHttpClientTests.Services(IncomingHttpClientTests.Settings(receiver.Url, certificates));

        var response = await gateway.GetRequiredService<IIncomingCoreClient>().SubmitAsync(Participant, Pacs008(), default);

        Assert.Equal(answer == "throw" ? 500 : 200, response.StatusCode);
        var result = Interpret(response);
        Assert.Equal(outcome, result.Status);
        if (outcome == CoreOutcome.Rejected)
        {
            Assert.Equal("AC04", result.ReasonCode);
        }
    }

    [Fact]
    public async Task The_status_query_answers_the_stored_result_and_404_for_a_message_never_received()
    {
        await using var receiver = await ReceiverHost.StartAsync();
        using var certificates = new TransportCertificates();
        using var gateway = IncomingHttpClientTests.Services(IncomingHttpClientTests.Settings(receiver.Url, certificates));
        var core = gateway.GetRequiredService<IIncomingTransferCoreClient>();
        var transfer = Transfer("pacs.009");
        await core.SubmitAsync(Participant, transfer, default);

        var known = await core.QueryAsync(Participant, "pacs.009", transfer.Key, default);
        var unknown = await core.QueryAsync(Participant, "pacs.009", "NEVER-SENT", default);

        Assert.Equal(CoreOutcome.Accepted, InterpretTransfer(known, transfer).Status);
        Assert.Equal(404, unknown.StatusCode);
        var query = receiver.Script.Queries.Last();
        Assert.Equal((IpsMessageKind.Pacs009, "NEVER-SENT", (string?)null), (query.Kind, query.Reference, query.HeaderKey));
    }

    [Fact]
    public async Task A_status_callback_and_a_reversal_are_delivered_with_their_keys_and_answered_204()
    {
        await using var receiver = await ReceiverHost.StartAsync();
        using var certificates = new TransportCertificates();
        using var outgoing = OutgoingHttpClientTests.Services(OutgoingHttpClientTests.Settings(receiver.Url, certificates));
        using var incoming = IncomingHttpClientTests.Services(IncomingHttpClientTests.Settings(receiver.Url, certificates));
        var status = new OutgoingStatus(Guid.NewGuid(), 1, "pacs.008", "CLIENT-1", DomainTransactionStatus.Accepted, DateTimeOffset.UtcNow, new(), "MSG-1", "E2E-1");

        var delivered = await outgoing.GetRequiredService<IOutgoingStatusReceiver>().SendAsync(status, status.IdempotencyKey, default);
        var reversal = await incoming.GetRequiredService<IIncomingReversalClient>().RequestAsync(
            new ReversalNotification(Guid.NewGuid(), Participant, "E2E-IN-9", DateTimeOffset.UtcNow, "MS03", "Late credit", "MSG-IN-9"), default);

        Assert.Equal(204, delivered);
        Assert.Equal(204, reversal.StatusCode);
        Assert.Collection(receiver.Script.Calls,
            call => Assert.Equal(("CLIENT-1:Accepted", "CLIENT-1:Accepted"), (call.HeaderKey, call.ContractKey)),
            call => Assert.Equal(("in:E2E-IN-9:Rejected", "in:E2E-IN-9:Rejected"), (call.HeaderKey, call.ContractKey)));
    }

    [Fact]
    public async Task Answers_use_the_contract_json_whatever_the_application_json_options_are()
    {
        await using var receiver = await ReceiverHost.StartAsync(services =>
            services.Configure<JsonOptions>(options => options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower));
        receiver.Script.Answer = (_, key) => Pacs008PaymentResultDto.Accept(null, key, null);
        using var http = new HttpClient { BaseAddress = new Uri(receiver.Url) };

        using var accepted = await http.PostAsync("/api/ips/pacs008/receive",
            new StringContent("""{"endToEndId":"E2E-RAW","amount":null,"unknownMember":1}""", Encoding.UTF8, "application/json"));
        using var malformed = await http.PostAsync("/api/ips/pacs008/receive", new StringContent("{not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var body = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        Assert.Equal("ACCP", body.RootElement.GetProperty("Status").GetString());
        Assert.Equal("E2E-RAW", body.RootElement.GetProperty("EndToEndId").GetString());
        Assert.False(body.RootElement.TryGetProperty("ReasonCode", out _));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("application/problem+json", malformed.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Exactly_the_interface_operations_are_mapped_at_their_contract_routes()
    {
        await using var receiver = await ReceiverHost.StartAsync();

        var mapped = receiver.Endpoints
            .Select(endpoint => (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Single(), "/" + endpoint.RoutePattern.RawText!.TrimStart('/')))
            .Order()
            .ToArray();
        var declared = typeof(IClientPaymentReceiver).GetMethods()
            .Select(method => method.GetCustomAttributes(typeof(RestEndpointAttribute), false).Cast<RestEndpointAttribute>().Single())
            .Select(endpoint => (endpoint.HttpMethod, endpoint.Route))
            .Order()
            .ToArray();
        Assert.Equal(9, declared.Length);
        Assert.Equal(declared, mapped);
    }

    [Fact]
    public void Mapping_without_registering_a_receiver_fails_at_startup()
    {
        var builder = WebApplication.CreateSlimBuilder();
        var app = builder.Build();

        var error = Assert.Throws<InvalidOperationException>(() => app.MapIpsCoreReceiver());

        Assert.Contains(nameof(IpsCoreReceiverExtensions.AddIpsCoreReceiver), error.Message, StringComparison.Ordinal);
    }

    private static CorePaymentResult Interpret(CoreResponse response) =>
        new IncomingCoreReplyInterpreter().Interpret(new CoreCallCompletion(response, null, DateTimeOffset.UtcNow),
            new IncomingPacs008Reference("BAH-008", "MSG-008", "E2E-008", "TX-008", null, null, null, null, null, null));

    private static CorePaymentResult InterpretTransfer(CoreResponse response, IIncomingTransferContent transfer) =>
        ((IIncomingTransferReplyInterpreter)new IncomingCoreReplyInterpreter()).Interpret(new CoreCallCompletion(response, null, DateTimeOffset.UtcNow), transfer);

    private static Pacs008Request Pacs008() => new()
    {
        EndToEndId = "E2E-008",
        InstructionId = "INSTR-008",
        Amount = 12.34m,
        Currency = "GEL"
    };

    private static IIncomingTransferContent Transfer(string kind) => kind switch
    {
        "pacs.009" => new IncomingPacs009(
            "MSG-IN-1", "E2E-IN-1", "INSTR-1", "TX-IN-1", null, 1, new DateOnly(2026, 10, 4), "GEL", 100.5m,
            new IncomingAgent(Other, null), new IncomingAgent(Participant, null), null, null, null, "GE95TB0000000123456789", "GE29NB0000000101904917"),
        "pacs.004" => new IncomingPacs004(
            "RTR-IN-1", 100.5m, "GEL", new DateOnly(2026, 10, 4), "FOCR", null, Participant, Other, null,
            new IncomingReturnParty("Original Payer", null, null, "GE95TB0000000123456789"),
            new IncomingReturnParty("Original Payee", null, null, "GE29NB0000000101904917"),
            new IncomingOriginalPayment("ORIG-TX-1", null, "ORIG-E2E-1", null, new DateOnly(2026, 10, 3), null, null, null, 100.5m, "GEL")),
        "pain.001" => new IncomingPain001(
            "PAIN-MSG-1", DateTimeOffset.UtcNow, new IncomingInitiatingParty("PISP LLC", "PISPGE22"), "PMTINF-1", null, null, null, null,
            new Pacs008DebtorInput { Name = "Payer", ParticipantBic = Participant, Account = "GE29NB0000000101904917" }, null, null, "E2E-PAIN-1",
            25.5m, "GEL", null, null, null, null),
        "camt.056" => new IncomingCamt056(
            "RCL-IN-1", DateTimeOffset.UtcNow, "CXL-IN-1", "P008-IN-1", "E2E-IN-1", "TX-IN-1", 100.5m, "GEL", new DateOnly(2026, 10, 3), "DUPL",
            new RecallOriginalInput { DebtorAgent = new() { Bic = Other }, CreditorAgent = new() { Bic = Participant } }),
        "camt.055" => new IncomingCamt055(
            "CNCL-IN-1", "PISPGE22", Participant, DateTimeOffset.UtcNow, null, "PMTINF-1", null, null, null, "PISP-E2E-1",
            new IncomingCancellationReason(null, null, "CUST", "Customer asked"),
            new IncomingCancelledInitiation("GEL", 25.5m, null, null, null, null, null, new RecallAgentInput { Bic = Participant }, null, null, null)),
        "camt.029" => new IncomingCamt029(
            "RFS-IN-1", DateTimeOffset.UtcNow, "CXLSTS-1", "RCL-OUT-1", "E2E-OUT-1", "TX-OUT-1", "CUST", "Refused",
            new Camt029OriginalInput { DebtorAgent = new() { Bic = Participant }, CreditorAgent = new() { Bic = Other } }, "RECALL-CLIENT-1"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private sealed class ReceiverHost(WebApplication app, ReceiverScript script) : IAsyncDisposable
    {
        public string Url => app.Urls.Single();

        public ReceiverScript Script { get; } = script;

        public IEnumerable<RouteEndpoint> Endpoints =>
            app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

        public static async Task<ReceiverHost> StartAsync(Action<IServiceCollection>? configure = null)
        {
            var script = new ReceiverScript();
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(script);
            builder.Services.AddIpsCoreReceiver<ScriptedReceiver>();
            configure?.Invoke(builder.Services);
            var app = builder.Build();
            app.MapIpsCoreReceiver();
            await app.StartAsync();
            return new(app, script);
        }

        public async ValueTask DisposeAsync() => await app.DisposeAsync();
    }

    private sealed record ReceivedCall(string Operation, string? HeaderKey, string? ContractKey);

    private sealed record StatusQuery(IpsMessageKind? Kind, string? Reference, string? HeaderKey);

    private sealed class ReceiverScript
    {
        private readonly ConcurrentDictionary<string, Pacs008PaymentResultDto> _answers = new();

        public ConcurrentQueue<ReceivedCall> Calls { get; } = new();

        public ConcurrentQueue<StatusQuery> Queries { get; } = new();

        public Func<object, string?, Pacs008PaymentResultDto> Answer { get; set; } =
            (_, key) => Pacs008PaymentResultDto.Accept(null, null, null, "POSTING-" + key);

        public Pacs008PaymentResultDto Receive(string operation, object message, string? headerKey, string? contractKey)
        {
            Calls.Enqueue(new(operation, headerKey, contractKey));
            var answer = Answer(message, contractKey);
            _answers[contractKey!] = answer;
            return answer;
        }

        public Pacs008PaymentResultDto? Find(InboundPaymentStatusQueryDto query, string? headerKey)
        {
            Queries.Enqueue(new(query.MessageKind, query.Reference, headerKey));
            return query.Reference is { } reference && _answers.TryGetValue(reference, out var answer) ? answer : null;
        }
    }

    private sealed class ScriptedReceiver(ReceiverScript script, IIpsReceiveContext context) : IClientPaymentReceiver
    {
        public Task<Pacs008PaymentResultDto> ReceivePacs008Async(Pacs008InstantPaymentRequestDto payment, CancellationToken cancellationToken) =>
            Task.FromResult(script.Receive(nameof(ReceivePacs008Async), payment, context.IdempotencyKey, IpsIdempotencyKeys.For(payment)));

        public Task<Pacs008PaymentResultDto> ReceivePacs009Async(Pacs009PaymentRequestDto payment, CancellationToken cancellationToken) =>
            Task.FromResult(script.Receive(nameof(ReceivePacs009Async), payment, context.IdempotencyKey, IpsIdempotencyKeys.For(payment)));

        public Task<Pacs008PaymentResultDto> ReceivePacs004Async(Pacs004PaymentReturnRequestDto payment, CancellationToken cancellationToken) =>
            Task.FromResult(script.Receive(nameof(ReceivePacs004Async), payment, context.IdempotencyKey, IpsIdempotencyKeys.For(payment)));

        public Task<Pacs008PaymentResultDto> ReceivePain001Async(Pain001PaymentInitiationDto initiation, CancellationToken cancellationToken) =>
            Task.FromResult(script.Receive(nameof(ReceivePain001Async), initiation, context.IdempotencyKey, IpsIdempotencyKeys.For(initiation)));

        public Task<Pacs008PaymentResultDto> ReceiveCamt056Async(Camt056RecallRequestDto recall, CancellationToken cancellationToken) =>
            Task.FromResult(script.Receive(nameof(ReceiveCamt056Async), recall, context.IdempotencyKey, IpsIdempotencyKeys.For(recall)));

        public Task<Pacs008PaymentResultDto> ReceiveCamt055Async(Camt055CancellationRequestDto cancellation, CancellationToken cancellationToken) =>
            Task.FromResult(script.Receive(nameof(ReceiveCamt055Async), cancellation, context.IdempotencyKey, IpsIdempotencyKeys.For(cancellation)));

        public Task<Pacs008PaymentResultDto> ReceiveCamt029Async(Camt029ResolutionOfInvestigationDto refusal, CancellationToken cancellationToken) =>
            Task.FromResult(script.Receive(nameof(ReceiveCamt029Async), refusal, context.IdempotencyKey, IpsIdempotencyKeys.For(refusal)));

        public Task ReceiveTransactionStatusAsync(TransactionStatusDto status, CancellationToken cancellationToken)
        {
            script.Calls.Enqueue(new(nameof(ReceiveTransactionStatusAsync), context.IdempotencyKey, IpsIdempotencyKeys.For(status)));
            return Task.CompletedTask;
        }

        public Task<Pacs008PaymentResultDto?> GetPaymentStatusAsync(InboundPaymentStatusQueryDto query, CancellationToken cancellationToken) =>
            Task.FromResult(script.Find(query, context.IdempotencyKey));
    }
}
