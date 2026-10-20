using System.Net.Http.Json;
using System.Text.Json;
using IPS.MiidleWear.Contracts.Http;

namespace IPS.MiidleWear.CoreAPI.Sending;

// What was sent and the gateway's answer, unchanged: 200 with the final status, 504 with the status while still waiting,
// 400 with the validation problem.
public sealed record SendResult(object Request, int StatusCode, JsonElement? Response);

// Posts a send to the gateway with its JSON conventions (IpsJson), as a core system does.
public sealed class GatewaySender(HttpClient client)
{
    public async Task<SendResult> SendAsync<TRequest>(string route, TRequest request, CancellationToken cancellationToken)
        where TRequest : notnull
    {
        using var response = await client.PostAsJsonAsync(route.TrimStart('/'), request, IpsJson.SerializerOptions, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new SendResult(request, (int)response.StatusCode, Parse(body));
    }

    private static JsonElement? Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(body);
        }
    }
}
