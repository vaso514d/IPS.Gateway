namespace IPS.Middleware.Infrastructure.Transport;

internal static class TransportPath
{
    internal static void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('?') || path.Contains('#') || path.Contains(':') || path.Contains('\\') ||
            !Uri.TryCreate(path.TrimStart('/'), UriKind.Relative, out _) || path.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidOperationException("Transport paths must be relative paths without query or fragment.");
    }
}
