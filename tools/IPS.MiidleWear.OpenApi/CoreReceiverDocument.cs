using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using IPS.MiidleWear.Contracts.Transactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

namespace IPS.MiidleWear.OpenApi;

// The OpenAPI 3.0 description of the endpoints a core system implements, produced from the endpoints the receiver package
// maps. Only what the endpoint metadata cannot carry is added here: the document header, the Idempotency-Key header and
// examples taken from the Contracts wire cases.
public static class CoreReceiverDocument
{
    public const string Name = "ips-core-receiver";
    public static readonly string RelativePath = Path.Combine("docs", "openapi", Name + ".json");
    private static readonly string WireCasesPath = Path.Combine("tests", "IPS.Middleware.Tests", "Baselines", "wire-cases.json");

    // The key the gateway sends with each delivery, as the operation's header description.
    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        ["ReceivePacs008"] = "The payment's endToEndId.",
        ["ReceivePacs009"] = "The transfer's endToEndId.",
        ["ReceivePacs004"] = "The return's id.",
        ["ReceivePain001"] = "The initiation's paymentInformationId.",
        ["ReceiveCamt056"] = "The recall's id (Assgnmt/Id).",
        ["ReceiveCamt055"] = "The cancellation request's msgId (Assgnmt/Id).",
        ["ReceiveCamt029"] = "The refusal's id (Assgnmt/Id).",
        ["ReceiveTransactionStatus"] = "{clientReference}:{status} for a transaction the core system sent; in:{coreReference}:{status} for the reversal of an incoming payment."
    };

    // The wire case used as the example of each operation's request body.
    private static readonly Dictionary<string, string> RequestExamples = new(StringComparer.Ordinal)
    {
        ["ReceivePacs008"] = "pacs008 nested parties and remittance",
        ["ReceivePacs009"] = "pacs009 dates times and uppercase property names",
        ["ReceivePacs004"] = "pacs004 return",
        ["ReceivePain001"] = "pain001 initiation",
        ["ReceiveCamt056"] = "camt056 delivered recall request",
        ["ReceiveCamt055"] = "camt055 delivered cancellation request",
        ["ReceiveCamt029"] = "camt029 delivered refusal of our recall",
        ["ReceiveTransactionStatus"] = "transaction outgoing accepted"
    };

    private const string AnswerExample = "pacs008 core result";

    public static async Task<string> GenerateAsync(string repositoryRoot)
    {
        var examples = ReadWireCases(Path.Combine(repositoryRoot, WireCasesPath));
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddIpsCoreReceiver<DescriptionOnlyReceiver>();
        builder.Services.AddOpenApi(Name, options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
            options.AddDocumentTransformer((document, _, _) => DescribeDocument(document));
            options.AddOperationTransformer((operation, _, _) => DescribeOperation(operation, examples));
        });
        await using var app = builder.Build();
        app.MapIpsCoreReceiver();
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        var json = await client.GetStringAsync($"/openapi/{Name}.json");
        return JsonNode.Parse(json)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).ReplaceLineEndings("\n") + "\n";
    }

    public static string FindRepositoryRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IPS.Middleware.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No IPS.Middleware.slnx above {start}.");
    }

    private static Task DescribeDocument(OpenApiDocument document)
    {
        document.Info = new OpenApiInfo
        {
            Title = "IPS core receiver",
            Version = ContractsVersion(),
            Description = "The endpoints a core banking system implements and the IPS middleware (the gateway) calls: incoming payments, " +
                "recalls, initiations and refusals, the status callback for transactions the core system sent, and the payment-status " +
                "query for a delivery the gateway got no final answer to. Host them with the IPS.MiidleWear.AspNetCore package " +
                "(AddIpsCoreReceiver and MapIpsCoreReceiver over an IClientPaymentReceiver from IPS.MiidleWear.Contracts). " +
                "Each operation's description states the rules the gateway applies to the answer."
        };
        document.Servers =
        [
            new OpenApiServer
            {
                Url = "https://{bankHost}",
                Description = "The core system's receiver, configured in the gateway as the CBS base URL.",
                Variables = new Dictionary<string, OpenApiServerVariable> { ["bankHost"] = new() { Default = "core.bank.example" } }
            }
        ];
        return Task.CompletedTask;
    }

    private static Task DescribeOperation(OpenApiOperation operation, IReadOnlyDictionary<string, JsonNode> examples)
    {
        var name = operation.OperationId!;
        if (Keys.TryGetValue(name, out var key))
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = IpsHttpHeaders.IdempotencyKey,
                In = ParameterLocation.Header,
                Required = true,
                Description = key + " The same key is a redelivery of the same message and must get the first answer.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
        }

        if (RequestExamples.TryGetValue(name, out var request) && operation.RequestBody?.Content is { } content)
        {
            foreach (var media in content.Values)
            {
                media.Example = examples[request].DeepClone();
            }
        }

        if (operation.Responses?.TryGetValue("200", out var answer) == true && answer.Content is { } answers)
        {
            foreach (var media in answers.Values)
            {
                media.Example = examples[AnswerExample].DeepClone();
            }
        }

        return Task.CompletedTask;
    }

    // Examples are the serialized form of the wire cases, so they show exactly what the gateway sends and reads.
    private static Dictionary<string, JsonNode> ReadWireCases(string path)
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(path));
        return cases.RootElement.EnumerateArray().ToDictionary(
            item => item.GetProperty("name").GetString()!,
            item => JsonNode.Parse(item.GetProperty("expected").GetString()!)!,
            StringComparer.Ordinal);
    }

    private static string ContractsVersion()
    {
        var version = typeof(IClientPaymentReceiver).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        return version.Split('+')[0];
    }

    // Registered only so the endpoints can be mapped and described; the document host never serves a call.
    private sealed class DescriptionOnlyReceiver : IClientPaymentReceiver
    {
        private static NotSupportedException NotServed() => new("This receiver only describes the endpoints.");

        public Task<Pacs008PaymentResultDto> ReceivePacs008Async(Pacs008InstantPaymentRequestDto payment, CancellationToken cancellationToken) => throw NotServed();

        public Task<Pacs008PaymentResultDto> ReceivePacs009Async(Pacs009PaymentRequestDto payment, CancellationToken cancellationToken) => throw NotServed();

        public Task<Pacs008PaymentResultDto> ReceivePacs004Async(Pacs004PaymentReturnRequestDto payment, CancellationToken cancellationToken) => throw NotServed();

        public Task<Pacs008PaymentResultDto> ReceivePain001Async(Pain001PaymentInitiationDto initiation, CancellationToken cancellationToken) => throw NotServed();

        public Task<Pacs008PaymentResultDto> ReceiveCamt056Async(Camt056RecallRequestDto recall, CancellationToken cancellationToken) => throw NotServed();

        public Task<Pacs008PaymentResultDto> ReceiveCamt055Async(Camt055CancellationRequestDto cancellation, CancellationToken cancellationToken) => throw NotServed();

        public Task<Pacs008PaymentResultDto> ReceiveCamt029Async(Camt029ResolutionOfInvestigationDto refusal, CancellationToken cancellationToken) => throw NotServed();

        public Task ReceiveTransactionStatusAsync(TransactionStatusDto status, CancellationToken cancellationToken) => throw NotServed();

        public Task<Pacs008PaymentResultDto?> GetPaymentStatusAsync(InboundPaymentStatusQueryDto query, CancellationToken cancellationToken) => throw NotServed();
    }
}
