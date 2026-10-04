using System.Text.Json;
using System.Text.Json.Serialization;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Persistence;

/// <summary>Explicit camel-case JSON for stored payment artifacts; CLR type names are never written.</summary>
internal static class PaymentJson
{
    private const int AcceptedVersion = 1;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    internal static T? Read<T>(string? json) where T : class => json is null ? null : JsonSerializer.Deserialize<T>(json, Options);

    internal static string WriteAccepted(AcceptedPacs008 accepted) => Write(new AcceptedSnapshot(AcceptedVersion, accepted));

    internal static AcceptedPacs008? ReadAccepted(string? json) => Read<AcceptedSnapshot>(json) switch
    {
        null => null,
        { Version: AcceptedVersion } snapshot => snapshot.Accepted,
        { Version: var version } => throw new NotSupportedException($"Accepted payment snapshot version {version} is not supported.")
    };

    private sealed record AcceptedSnapshot(int Version, AcceptedPacs008 Accepted);
}
