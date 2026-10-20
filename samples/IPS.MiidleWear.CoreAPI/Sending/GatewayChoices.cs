using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace IPS.MiidleWear.CoreAPI.Sending;

// Lists the configured creditor banks and currencies as the allowed values of the send parameters, so Swagger UI offers
// them in a drop-down instead of a text box.
internal static class GatewayChoices
{
    internal static Task Apply(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var gateway = context.ApplicationServices.GetRequiredService<IOptions<GatewayOptions>>().Value;
        foreach (var parameter in operation.Parameters ?? [])
        {
            var choices = parameter.Name switch
            {
                "creditorBic" => gateway.CreditorBanks.Keys.ToArray(),
                "currency" => gateway.Currencies,
                "reasonCode" => gateway.RecallReasons,
                _ => null
            };
            if (choices is not null && parameter is OpenApiParameter { Schema: OpenApiSchema schema })
            {
                schema.Enum = [.. choices.Select(choice => (JsonNode)JsonValue.Create(choice))];
            }
        }

        return Task.CompletedTask;
    }
}
