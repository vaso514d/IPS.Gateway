namespace IPS.MiidleWear.CoreReceiver.Sample;

// appsettings.json "Sample": how the sample core answers, so each path the gateway can see is easy to try.
public sealed class SampleOptions
{
    public const string Section = "Sample";

    // Per kind ("pacs.008", "pacs.009", ...): "Accept" (default) or "Reject:<reason code>", for example "Reject:AC04".
    public Dictionary<string, string> Answers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    // How long a posting takes. While it runs, a redelivery waits for it and the payment-status query answers PDNG.
    public TimeSpan PostingDelay { get; init; } = TimeSpan.Zero;
}
