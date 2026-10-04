namespace IPS.Middleware.Infrastructure.Payments.Pacs008;

internal static class ProtocolTextChunks
{
    internal const int AddressLineLength = 70;
    internal const int RemittanceLineLength = 140;

    internal static IEnumerable<string> Split(string? text, int length)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        for (var offset = 0; offset < text.Length; offset += length)
            yield return text.Substring(offset, Math.Min(length, text.Length - offset));
    }
}
