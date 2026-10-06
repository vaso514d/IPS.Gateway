using System.Text.Json;
using System.Text.Json.Serialization;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Camt029;
using IPS.Middleware.Application.Payments.Camt056;
using IPS.Middleware.Application.Payments.Pacs004;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pacs009;

namespace IPS.Middleware.Infrastructure.Persistence;
// Explicit camel-case JSON for stored payment artifacts; CLR type names are never written.
internal static class PaymentJson
{
    private const int AcceptedVersion = 1;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    internal static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    internal static T? Read<T>(string? json)
        where T : class => json is null ? null : JsonSerializer.Deserialize<T>(json, Options);
    // The snapshot is stored under its message type, which selects the concrete type when it is read back.
    internal static string WriteAccepted(string messageType, IAcceptedPayment accepted) => messageType switch
    {
        PaymentMessageTypes.Pacs008 => Write(new AcceptedSnapshot<AcceptedPacs008>(AcceptedVersion, (AcceptedPacs008)accepted)),
        PaymentMessageTypes.Pacs009 => Write(new AcceptedSnapshot<AcceptedPacs009>(AcceptedVersion, (AcceptedPacs009)accepted)),
        PaymentMessageTypes.Pacs004 => Write(new AcceptedSnapshot<AcceptedPacs004>(AcceptedVersion, (AcceptedPacs004)accepted)),
        PaymentMessageTypes.Camt056 => Write(new AcceptedSnapshot<AcceptedCamt056>(AcceptedVersion, (AcceptedCamt056)accepted)),
        PaymentMessageTypes.Camt029 => Write(new AcceptedSnapshot<AcceptedCamt029>(AcceptedVersion, (AcceptedCamt029)accepted)),
        _ => throw new ArgumentOutOfRangeException(nameof(messageType), messageType, "No accepted snapshot for this message type.")
    };
    internal static IAcceptedPayment? ReadAccepted(string messageType, string? json) => messageType switch
    {
        PaymentMessageTypes.Pacs008 => ReadSnapshot<AcceptedPacs008>(json),
        PaymentMessageTypes.Pacs009 => ReadSnapshot<AcceptedPacs009>(json),
        PaymentMessageTypes.Pacs004 => ReadSnapshot<AcceptedPacs004>(json),
        PaymentMessageTypes.Camt056 => ReadSnapshot<AcceptedCamt056>(json),
        PaymentMessageTypes.Camt029 => ReadSnapshot<AcceptedCamt029>(json),
        _ => null
    };
    private static T? ReadSnapshot<T>(string? json)
        where T : class, IAcceptedPayment => Read<AcceptedSnapshot<T>>(json) switch
        {
            null => null,
            { Version: AcceptedVersion } snapshot => snapshot.Accepted,
            { Version: var version } => throw new NotSupportedException($"Accepted payment snapshot version {version} is not supported.")
        };
    private sealed record AcceptedSnapshot<T>(int Version, T Accepted);
}
