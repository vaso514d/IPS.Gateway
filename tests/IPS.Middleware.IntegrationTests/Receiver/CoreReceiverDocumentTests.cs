using System.Text.Json;
using System.Text.Json.Serialization;
using IPS.MiidleWear.Contracts.Camt029;
using IPS.MiidleWear.Contracts.Camt055;
using IPS.MiidleWear.Contracts.Camt056;
using IPS.MiidleWear.Contracts.Http;
using IPS.MiidleWear.Contracts.Pacs004;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;
using IPS.MiidleWear.Contracts.Pain001;
using IPS.MiidleWear.Contracts.Transactions;
using IPS.MiidleWear.OpenApi;
using Microsoft.OpenApi;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Receiver;

// docs/openapi/ips-core-receiver.json is generated from the receiver package's endpoints (017b). Regenerate it with
// "dotnet run --project tools/IPS.MiidleWear.OpenApi" after a deliberate contract change.
public sealed class CoreReceiverDocumentTests
{
    private static readonly string Root = CoreReceiverDocument.FindRepositoryRoot(AppContext.BaseDirectory);

    private static readonly Dictionary<string, Type> Bodies = new(StringComparer.Ordinal)
    {
        ["ReceivePacs008"] = typeof(Pacs008InstantPaymentRequestDto),
        ["ReceivePacs009"] = typeof(Pacs009PaymentRequestDto),
        ["ReceivePacs004"] = typeof(Pacs004PaymentReturnRequestDto),
        ["ReceivePain001"] = typeof(Pain001PaymentInitiationDto),
        ["ReceiveCamt056"] = typeof(Camt056RecallRequestDto),
        ["ReceiveCamt055"] = typeof(Camt055CancellationRequestDto),
        ["ReceiveCamt029"] = typeof(Camt029ResolutionOfInvestigationDto),
        ["ReceiveTransactionStatus"] = typeof(TransactionStatusDto)
    };

    [Fact]
    public async Task The_committed_document_is_the_generated_one()
    {
        var committed = (await File.ReadAllTextAsync(Path.Combine(Root, CoreReceiverDocument.RelativePath))).ReplaceLineEndings("\n");

        Assert.True(committed == await CoreReceiverDocument.GenerateAsync(Root),
            "docs/openapi/ips-core-receiver.json is stale: run dotnet run --project tools/IPS.MiidleWear.OpenApi and review the change.");
    }

    [Fact]
    public async Task The_document_is_valid_openapi_3_0_with_exactly_the_nine_receiver_operations()
    {
        var result = OpenApiDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Root, CoreReceiverDocument.RelativePath)), OpenApiConstants.Json);

        Assert.Empty(result.Diagnostic!.Errors);
        Assert.Equal(OpenApiSpecVersion.OpenApi3_0, result.Diagnostic.SpecificationVersion);
        var operations = result.Document!.Paths
            .SelectMany(path => path.Value.Operations!.Select(operation => (Method: operation.Key.Method, path.Key, operation.Value.OperationId)))
            .Order()
            .ToArray();
        (string Method, string Path, string? OperationId)[] expected =
        [
            ("GET", "/api/ips/payments/status", "GetPaymentStatus"),
            ("POST", "/api/ips/camt029/receive", "ReceiveCamt029"),
            ("POST", "/api/ips/camt055/receive", "ReceiveCamt055"),
            ("POST", "/api/ips/camt056/receive", "ReceiveCamt056"),
            ("POST", "/api/ips/pacs004/receive", "ReceivePacs004"),
            ("POST", "/api/ips/pacs008/receive", "ReceivePacs008"),
            ("POST", "/api/ips/pacs009/receive", "ReceivePacs009"),
            ("POST", "/api/ips/pain001/receive", "ReceivePain001"),
            ("POST", "/api/ips/transactions/status/receive", "ReceiveTransactionStatus")
        ];
        Assert.Equal(expected, operations);
    }

    [Fact]
    public async Task Every_example_reads_as_its_contract_type_without_unknown_members()
    {
        var strict = new JsonSerializerOptions(IpsJson.SerializerOptions) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Root, CoreReceiverDocument.RelativePath)));
        var checkedExamples = 0;

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                var id = operation.Value.GetProperty("operationId").GetString()!;
                if (Bodies.TryGetValue(id, out var body))
                {
                    var example = operation.Value.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("example");
                    Assert.NotNull(example.Deserialize(body, strict));
                    checkedExamples++;
                }

                if (operation.Value.GetProperty("responses").TryGetProperty("200", out var answer))
                {
                    var example = answer.GetProperty("content").GetProperty("application/json").GetProperty("example");
                    Assert.NotNull(example.Deserialize<Pacs008PaymentResultDto>(strict));
                    checkedExamples++;
                }

                if (id != "GetPaymentStatus")
                {
                    Assert.Contains(operation.Value.GetProperty("parameters").EnumerateArray(),
                        parameter => parameter.GetProperty("name").GetString() == IpsHttpHeaders.IdempotencyKey
                            && parameter.GetProperty("in").GetString() == "header");
                }
            }
        }

        Assert.Equal(8 + 8, checkedExamples);
    }
}
