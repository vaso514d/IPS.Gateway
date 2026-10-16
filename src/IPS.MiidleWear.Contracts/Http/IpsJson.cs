using System.Text.Json;
using System.Text.Json.Serialization;

namespace IPS.MiidleWear.Contracts.Http;

/// <summary>
/// The JSON conventions of the gateway's wire format, for a core system that reads what the gateway sends and writes
/// its answers: web defaults (case-insensitive reading), property names from <see cref="JsonPropertyNameAttribute"/>,
/// enums as strings through the converters declared on them, and <c>null</c> members omitted when writing. The gateway
/// itself sends <c>null</c> members, which these options accept.
/// <para>
/// Serializers that ignore System.Text.Json attributes (for example Newtonsoft.Json) produce other names and numeric
/// enums; use these options for the contract types instead.
/// </para>
/// </summary>
public static class IpsJson
{
    /// <summary>Read-only options; share this instance rather than copying it.</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
