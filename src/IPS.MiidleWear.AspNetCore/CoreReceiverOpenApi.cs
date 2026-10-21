using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using IPS.MiidleWear.Contracts.Abstractions;
using IPS.MiidleWear.Contracts.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace IPS.MiidleWear.AspNetCore;

/// <summary>The OpenAPI 3.0 document of the endpoints <see cref="IpsCoreReceiverExtensions.MapIpsCoreReceiver"/> maps.</summary>
public static class CoreReceiverOpenApi
{
    /// <summary>The document name; <c>MapOpenApi()</c> serves it at <c>/openapi/ips-core-receiver.json</c>.</summary>
    public const string DocumentName = "ips-core-receiver";

    // The key the gateway sends with each delivery, as the operation's header description.
    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        ["ReceiveIncomingPayment"] = "The payment's transactionId.",
        ["ReceiveFiTransfer"] = "The transfer's transactionId.",
        ["ReceivePacs004"] = "The return's id.",
        ["ReceivePain001"] = "The initiation's paymentInformationId.",
        ["ReceiveCamt056"] = "The recall's id (Assgnmt/Id).",
        ["ReceiveCamt055"] = "The cancellation request's msgId (Assgnmt/Id).",
        ["ReceiveCamt029"] = "The refusal's id (Assgnmt/Id).",
        ["ReceiveTransactionStatus"] = "{clientReference}:{status} for a transaction the core system sent; in:{coreReference}:{status} for the reversal of an incoming payment."
    };

    private static readonly Dictionary<string, object> RequestExamples = new(StringComparer.Ordinal)
    {
        ["ReceiveIncomingPayment"] = ReceiverExamples.IncomingPayment,
        ["ReceiveFiTransfer"] = ReceiverExamples.FiTransfer,
        ["ReceivePacs004"] = ReceiverExamples.Pacs004,
        ["ReceivePain001"] = ReceiverExamples.Pain001,
        ["ReceiveCamt056"] = ReceiverExamples.Camt056,
        ["ReceiveCamt055"] = ReceiverExamples.Camt055,
        ["ReceiveCamt029"] = ReceiverExamples.Camt029,
        ["ReceiveTransactionStatus"] = ReceiverExamples.TransactionStatus
    };

    /// <summary>
    /// Registers the <see cref="DocumentName"/> document: OpenAPI 3.0 with the receiver's header, the Idempotency-Key header
    /// of each delivery and typed examples of what the gateway sends and reads. Serve it with <c>app.MapOpenApi()</c>.
    /// </summary>
    public static IServiceCollection AddIpsCoreReceiverOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(DocumentName, options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
            // Only the receiver operations: the application's own endpoints, grouped or not, are not part of the contract.
            options.ShouldInclude = description => description.GroupName == DocumentName;
            options.AddDocumentTransformer((document, _, _) => DescribeDocument(document));
            options.AddOperationTransformer((operation, _, _) => DescribeOperation(operation));
        });

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

    private static Task DescribeOperation(OpenApiOperation operation)
    {
        var name = operation.OperationId ?? string.Empty;
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
                media.Example = Serialize(request);
            }
        }

        // The FI transfer's receive call answers 202; every other answer is 200.
        var success = name == "ReceiveFiTransfer" ? "202" : "200";
        if (operation.Responses?.TryGetValue(success, out var answer) == true && answer.Content is { } answers)
        {
            // The incoming instant payment and FI transfer have their own answers; the other kinds share one.
            object example = name switch
            {
                "ReceiveIncomingPayment" or "GetIncomingPayment" => ReceiverExamples.IncomingPaymentAnswer,
                "ReceiveFiTransfer" => ReceiverExamples.FiTransferAnswer,
                "GetFiTransfer" => ReceiverExamples.FiTransferStatus,
                _ => ReceiverExamples.Answer
            };
            foreach (var media in answers.Values)
            {
                media.Example = Serialize(example);
            }
        }

        return Task.CompletedTask;
    }

    // Serialized as the gateway writes and reads them, so the examples show the exact wire shape.
    private static JsonNode Serialize(object example) =>
        JsonSerializer.SerializeToNode(example, example.GetType(), IpsJson.SerializerOptions)!;

    private static string ContractsVersion()
    {
        var version = typeof(IClientPaymentReceiver).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        return version.Split('+')[0];
    }
}
