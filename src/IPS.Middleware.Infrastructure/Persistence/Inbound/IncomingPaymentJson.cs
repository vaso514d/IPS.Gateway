namespace IPS.Middleware.Infrastructure.Persistence.Inbound;
// Versioned JSON for the frozen incoming request and each receipt's original references.
internal static class IncomingPaymentJson
{
    private const int Version = 1;
    internal static string Write<T>(T value) => PaymentJson.Write(new Versioned<T>(Version, value));
    internal static T Read<T>(string json)
        where T : class => PaymentJson.Read<Versioned<T>>(json) switch
        {
            { Version: Version, Value: { } value } => value,
            var other => throw new NotSupportedException($"Incoming payment snapshot version {other?.Version} is not supported.")
        };
    private sealed record Versioned<T>(int Version, T Value);
}
