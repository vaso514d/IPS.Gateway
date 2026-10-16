using System.Reflection;
using System.Text.Json;
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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IPS.MiidleWear.AspNetCore;

// Each operation is listed once with a typed call; its route, method and description come from its RestEndpoint
// attribute. The listed set must equal the interface, so a Contracts operation this package does not map fails startup.
internal static class CoreReceiverEndpoints
{
    private const string Json = "application/json";
    private const string LoggerName = "IPS.MiidleWear.AspNetCore.CoreReceiver";

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        string[] mapped =
        [
            MapReceive<Pacs008InstantPaymentRequestDto>(endpoints, nameof(IClientPaymentReceiver.ReceivePacs008Async),
                static (receiver, message, token) => receiver.ReceivePacs008Async(message, token)),
            MapReceive<Pacs009PaymentRequestDto>(endpoints, nameof(IClientPaymentReceiver.ReceivePacs009Async),
                static (receiver, message, token) => receiver.ReceivePacs009Async(message, token)),
            MapReceive<Pacs004PaymentReturnRequestDto>(endpoints, nameof(IClientPaymentReceiver.ReceivePacs004Async),
                static (receiver, message, token) => receiver.ReceivePacs004Async(message, token)),
            MapReceive<Pain001PaymentInitiationDto>(endpoints, nameof(IClientPaymentReceiver.ReceivePain001Async),
                static (receiver, message, token) => receiver.ReceivePain001Async(message, token)),
            MapReceive<Camt056RecallRequestDto>(endpoints, nameof(IClientPaymentReceiver.ReceiveCamt056Async),
                static (receiver, message, token) => receiver.ReceiveCamt056Async(message, token)),
            MapReceive<Camt055CancellationRequestDto>(endpoints, nameof(IClientPaymentReceiver.ReceiveCamt055Async),
                static (receiver, message, token) => receiver.ReceiveCamt055Async(message, token)),
            MapReceive<Camt029ResolutionOfInvestigationDto>(endpoints, nameof(IClientPaymentReceiver.ReceiveCamt029Async),
                static (receiver, message, token) => receiver.ReceiveCamt029Async(message, token)),
            MapStatusCallback(endpoints),
            MapPaymentStatus(endpoints)
        ];
        RequireEveryOperation(mapped);
    }

    private static string MapReceive<TMessage>(
        IEndpointRouteBuilder endpoints,
        string operation,
        Func<IClientPaymentReceiver, TMessage, CancellationToken, Task<Pacs008PaymentResultDto>> receive)
        where TMessage : class
    {
        var endpoint = Endpoint(operation);
        Func<HttpContext, Task> handler = context => ReceiveAsync(context, receive);
        endpoints.MapMethods(endpoint.Route, [endpoint.HttpMethod], handler)
            .Describe(operation, endpoint)
            .Accepts<TMessage>(Json)
            .Produces<Pacs008PaymentResultDto>(StatusCodes.Status200OK, Json)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        return operation;
    }

    private static string MapStatusCallback(IEndpointRouteBuilder endpoints)
    {
        const string operation = nameof(IClientPaymentReceiver.ReceiveTransactionStatusAsync);
        var endpoint = Endpoint(operation);
        Func<HttpContext, Task> handler = ReceiveStatusAsync;
        endpoints.MapMethods(endpoint.Route, [endpoint.HttpMethod], handler)
            .Describe(operation, endpoint)
            .Accepts<TransactionStatusDto>(Json)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        return operation;
    }

    private static string MapPaymentStatus(IEndpointRouteBuilder endpoints)
    {
        const string operation = nameof(IClientPaymentReceiver.GetPaymentStatusAsync);
        var endpoint = Endpoint(operation);
        Func<IpsMessageKind?, string?, HttpContext, Task> handler = PaymentStatusAsync;
        endpoints.MapMethods(endpoint.Route, [endpoint.HttpMethod], handler)
            .Describe(operation, endpoint)
            .Produces<Pacs008PaymentResultDto>(StatusCodes.Status200OK, Json)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        return operation;
    }

    private static async Task ReceiveAsync<TMessage>(
        HttpContext context,
        Func<IClientPaymentReceiver, TMessage, CancellationToken, Task<Pacs008PaymentResultDto>> receive)
        where TMessage : class
    {
        var message = await ReadAsync<TMessage>(context);
        if (message is null)
        {
            await ProblemAsync(context, StatusCodes.Status400BadRequest, $"The body is not a readable {typeof(TMessage).Name}.");
            return;
        }

        var receiver = Receiver(context, context.Request.Headers[IpsHttpHeaders.IdempotencyKey]);
        await RunAsync(context, async token =>
        {
            var result = await receive(receiver, message, token);
            await WriteAsync(context, result, token);
        });
    }

    private static async Task ReceiveStatusAsync(HttpContext context)
    {
        var status = await ReadAsync<TransactionStatusDto>(context);
        if (status is null)
        {
            await ProblemAsync(context, StatusCodes.Status400BadRequest, $"The body is not a readable {nameof(TransactionStatusDto)}.");
            return;
        }

        var receiver = Receiver(context, context.Request.Headers[IpsHttpHeaders.IdempotencyKey]);
        await RunAsync(context, async token =>
        {
            await receiver.ReceiveTransactionStatusAsync(status, token);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });
    }

    private static async Task PaymentStatusAsync(IpsMessageKind? messageKind, string? reference, HttpContext context)
    {
        var query = new InboundPaymentStatusQueryDto { MessageKind = messageKind, Reference = reference };
        var receiver = Receiver(context, idempotencyKey: null);
        await RunAsync(context, async token =>
        {
            if (await receiver.GetPaymentStatusAsync(query, token) is { } result)
            {
                await WriteAsync(context, result, token);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
        });
    }

    // The receiver's own failure is answered 500, never a business answer: the gateway then asks again. A call the
    // gateway abandoned has nobody to answer.
    private static async Task RunAsync(HttpContext context, Func<CancellationToken, Task> call)
    {
        var token = context.RequestAborted;
        try
        {
            await call(token);
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerName)
                .LogError(error, "The core receiver failed on {Method} {Path}.", context.Request.Method, context.Request.Path);
            if (!context.Response.HasStarted)
            {
                await ProblemAsync(context, StatusCodes.Status500InternalServerError, "The core system could not process the call.");
            }
        }
    }

    private static IClientPaymentReceiver Receiver(HttpContext context, string? idempotencyKey)
    {
        context.RequestServices.GetRequiredService<IpsReceiveContext>().IdempotencyKey = idempotencyKey;
        return context.RequestServices.GetRequiredService<IClientPaymentReceiver>();
    }

    private static async Task<TMessage?> ReadAsync<TMessage>(HttpContext context)
        where TMessage : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<TMessage>(context.Request.Body, IpsJson.SerializerOptions, context.RequestAborted);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Task WriteAsync(HttpContext context, Pacs008PaymentResultDto result, CancellationToken token)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        return context.Response.WriteAsJsonAsync(result, IpsJson.SerializerOptions, token);
    }

    private static Task ProblemAsync(HttpContext context, int status, string title) =>
        TypedResults.Problem(statusCode: status, title: title).ExecuteAsync(context);

    private static RestEndpointAttribute Endpoint(string operation) =>
        typeof(IClientPaymentReceiver).GetMethod(operation)?.GetCustomAttribute<RestEndpointAttribute>()
        ?? throw new InvalidOperationException($"{nameof(IClientPaymentReceiver)}.{operation} has no [{nameof(RestEndpointAttribute)}].");

    private static RouteHandlerBuilder Describe(this RouteHandlerBuilder builder, string operation, RestEndpointAttribute endpoint)
    {
        builder.WithName(operation.EndsWith("Async", StringComparison.Ordinal) ? operation[..^"Async".Length] : operation);
        if (endpoint.Summary is { } summary)
        {
            builder.WithSummary(summary);
        }

        if (endpoint.Description is { } description)
        {
            builder.WithDescription(description);
        }

        if (endpoint.Tag is { } tag)
        {
            builder.WithTags(tag);
        }

        return builder;
    }

    private static void RequireEveryOperation(string[] mapped)
    {
        var declared = typeof(IClientPaymentReceiver).GetMethods().Select(method => method.Name);
        var missing = declared.Except(mapped, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"{nameof(IClientPaymentReceiver)} operations not mapped: {string.Join(", ", missing)}.");
        }
    }
}
