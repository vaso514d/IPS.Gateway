using System.Text.Json;
using IPS.Middleware.Application.Payments.Execution;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Infrastructure.Payments.StatusDelivery;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Transactions;
using Microsoft.AspNetCore.Mvc;

namespace IPS.Middleware.Api.Payments;

internal static class OutgoingEndpoints
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    internal static void MapOutgoing(this WebApplication app)
    {
        if (!app.Services.GetRequiredService<OutgoingExecutionOptions>().Enabled) return;
        app.MapPost(Pacs008RestApiRoutes.Send, SendAsync).WithName("SendPacs008")
            .Produces<TransactionStatusDto>(200).Produces<TransactionStatusDto>(504).ProducesValidationProblem();
        app.MapGet(TransactionRestApiRoutes.Status, ReadAsync).WithName("GetTransactionStatus")
            .Produces<TransactionStatusDto>().Produces(404).ProducesValidationProblem();
    }
    private static async Task<IResult> SendAsync(Pacs008InstantPaymentRequestDto request, OutgoingSubmission submission, CancellationToken token)
    {
        var result = await submission.SubmitAsync(Pacs008RequestMapping.Map(request), JsonSerializer.Serialize(request, JsonSerializerOptions.Web), token);
        return result.Status is { } status ? Results.Json(OutgoingStatusContract.Map(status), Wire, statusCode: result.TimedOut ? 504 : 200) : Invalid(result.Errors);
    }
    private static async Task<IResult> ReadAsync([FromQuery] string? messageKind, [FromQuery] string? clientReference,
        OutgoingStatusReader reader, CancellationToken token)
    {
        var type = Enum.TryParse<IpsMessageKind>(messageKind, true, out var kind) ? kind switch
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
        var errors = OutgoingStatusQuery.Validate(type, clientReference);
        if (errors.Count != 0) return Invalid(errors);
        var status = await reader.ReadAsync(type!, clientReference!, token);
        return status is null ? Results.NotFound() : Results.Ok(OutgoingStatusContract.Map(status));
    }
    private static IResult Invalid(IReadOnlyList<IntakeValidationError> errors) => Results.ValidationProblem(
        errors.GroupBy(e => e.Field).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()));
}
