namespace IPS.Middleware.Infrastructure.Persistence.Inbound;
/// <summary>Versioned JSON for the frozen incoming request and each receipt's original references.</summary>
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
    private sealed class Versioned<T>
    {
        public Versioned(int version, T value)
        {
            Version = version;
            Value = value;
        }

        public int Version { get; init; }
        public T Value { get; init; }
    }
}
