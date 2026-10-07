using IPS.Middleware.Application.Proxy;
using IPS.Middleware.Application.Transactions;
using IPS.MiidleWear.Contracts.Proxy;
using Microsoft.AspNetCore.Mvc;

namespace IPS.Middleware.Api.Proxy;

// The three Proxy Solution management operations. An accept and a reject are both 200; no answer from the Proxy Solution is 504
// (timed out) or 502 (not reachable or an error status), and the outcome of the operation is then unknown.
[ApiController]
public sealed class ProxyController(ProxyManagement management) : ControllerBase
{
    [HttpPost(ProxyRestApiRoutes.Register, Name = "RegisterProxy")]
    [ProducesResponseType<ProxyOperationResultDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status504GatewayTimeout, "application/problem+json")]
    public async Task<IResult> RegisterAsync([FromBody] ProxyRegisterRequestDto request, CancellationToken token) =>
        Respond(await management.RegisterAsync(ProxyRequestMapping.Map(request), token));

    [HttpPost(ProxyRestApiRoutes.Update, Name = "UpdateProxy")]
    [ProducesResponseType<ProxyOperationResultDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status504GatewayTimeout, "application/problem+json")]
    public async Task<IResult> UpdateAsync([FromBody] ProxyUpdateRequestDto request, CancellationToken token) =>
        Respond(await management.UpdateAsync(ProxyRequestMapping.Map(request), token));

    [HttpPost(ProxyRestApiRoutes.Remove, Name = "RemoveProxy")]
    [ProducesResponseType<ProxyOperationResultDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status504GatewayTimeout, "application/problem+json")]
    public async Task<IResult> RemoveAsync([FromBody] ProxyRemoveRequestDto request, CancellationToken token) =>
        Respond(await management.RemoveAsync(ProxyRequestMapping.Map(request), token));

    private static IResult Respond(ProxyManagementResult result)
    {
        if (result.Errors.Count != 0)
        {
            return Invalid(result.Errors);
        }

        return result.Delivery switch
        {
            ProxyDelivery.TimedOut => Results.Problem(
                "The Proxy Solution did not answer in time; the outcome of the operation is unknown.", statusCode: StatusCodes.Status504GatewayTimeout),
            ProxyDelivery.Failed => Results.Problem(
                "The Proxy Solution could not be reached or returned an error status; the outcome of the operation is unknown.", statusCode: StatusCodes.Status502BadGateway),
            _ => Results.Ok(new ProxyOperationResultDto(result.Outcome!.Accepted, result.Outcome.ErrorCode, result.Outcome.Description))
        };
    }

    private static IResult Invalid(IReadOnlyList<IntakeValidationError> errors)
    {
        var byField = errors
            .GroupBy(error => error.Field)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray());
        return Results.ValidationProblem(byField);
    }
}
