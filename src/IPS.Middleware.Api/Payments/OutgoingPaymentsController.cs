using System.Text.Json;
using System.Text.Json.Serialization;
using IPS.Middleware.Api.Binding;
using IPS.Middleware.Application.Payments.Execution;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Infrastructure.Payments.StatusDelivery;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;
using IPS.MiidleWear.Contracts.Transactions;
using Microsoft.AspNetCore.Mvc;

namespace IPS.Middleware.Api.Payments;

[ApiController]
public sealed class OutgoingPaymentsController(OutgoingSubmission submission, OutgoingStatusReader reader) : ControllerBase
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [HttpPost(Pacs008RestApiRoutes.Send, Name = "SendPacs008")]
    [ProducesResponseType<TransactionStatusDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<TransactionStatusDto>(StatusCodes.Status504GatewayTimeout, "application/json")]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IResult> SendAsync([FromBody] Pacs008InstantPaymentRequestDto request, CancellationToken token) =>
        Respond(await submission.SubmitAsync(Pacs008RequestMapping.Map(request), Json(request), token));

    [HttpPost(Pacs009RestApiRoutes.Send, Name = "SendPacs009")]
    [ProducesResponseType<TransactionStatusDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<TransactionStatusDto>(StatusCodes.Status504GatewayTimeout, "application/json")]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IResult> SendPacs009Async([FromBody] Pacs009PaymentRequestDto request, CancellationToken token) =>
        Respond(await submission.SubmitAsync(Pacs009RequestMapping.Map(request), Json(request), token));

    [HttpGet(TransactionRestApiRoutes.Status, Name = "GetTransactionStatus")]
    [ProducesResponseType<TransactionStatusDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IResult> ReadAsync(
        [FromQuery, ModelBinder(typeof(QueryStringValueBinder))] string? messageKind,
        [FromQuery, ModelBinder(typeof(QueryStringValueBinder))] string? clientReference,
        CancellationToken token)
    {
        var type = MessageType(messageKind);
        var errors = OutgoingStatusQuery.Validate(type, clientReference);
        if (errors.Count != 0)
        {
            return Invalid(errors);
        }

        var status = await reader.ReadAsync(type!, clientReference!, token);
        return status is null ? Results.NotFound() : Results.Ok(OutgoingStatusContract.Map(status));
    }

    private static string Json<T>(T request) => JsonSerializer.Serialize(request, JsonSerializerOptions.Web);

    private static IResult Respond(OutgoingSubmissionResult result)
    {
        if (result.Status is not { } status)
        {
            return Invalid(result.Errors);
        }

        var statusCode = result.TimedOut ? StatusCodes.Status504GatewayTimeout : StatusCodes.Status200OK;
        return Results.Json(OutgoingStatusContract.Map(status), Wire, statusCode: statusCode);
    }

    private static string? MessageType(string? messageKind)
    {
        return Enum.TryParse<IpsMessageKind>(messageKind, true, out var kind) ? kind switch
        {
            IpsMessageKind.Pacs008 => "pacs.008",
            IpsMessageKind.Pacs009 => "pacs.009",
            IpsMessageKind.Pacs004 => "pacs.004",
            IpsMessageKind.Camt056 => "camt.056",
            IpsMessageKind.Camt029 => "camt.029",
            IpsMessageKind.Pain002 => "pain.002",
            IpsMessageKind.Pain001 => "pain.001",
            _ => null
        } : null;
    }

    private static IResult Invalid(IReadOnlyList<IntakeValidationError> errors)
    {
        var byField = errors
            .GroupBy(error => error.Field)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray());
        return Results.ValidationProblem(byField);
    }
}
