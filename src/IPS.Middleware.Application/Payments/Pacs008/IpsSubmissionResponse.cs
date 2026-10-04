namespace IPS.Middleware.Application.Payments.Pacs008;

/// <summary>The received HTTP evidence, before interpreting its business meaning.</summary>
public sealed class IpsSubmissionResponse(int httpStatusCode, string body, IReadOnlyList<IpsResponseHeader> headers)
{
    public int HttpStatusCode { get; } = httpStatusCode is >= 100 and <= 599
        ? httpStatusCode : throw new ArgumentOutOfRangeException(nameof(httpStatusCode));
    public string Body { get; } = body ?? throw new ArgumentNullException(nameof(body));
    public IReadOnlyList<IpsResponseHeader> Headers { get; } = Snapshot(headers);

    // Copy before validating so later changes to the caller's list cannot alter stored evidence.
    private static IReadOnlyList<IpsResponseHeader> Snapshot(IReadOnlyList<IpsResponseHeader> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var snapshot = headers.ToArray();
        if (snapshot.Any(header => header is null || string.IsNullOrWhiteSpace(header.Name) || header.Value is null))
            throw new ArgumentException("Response headers require names and values.", nameof(headers));
        return Array.AsReadOnly(snapshot);
    }
}

public sealed record IpsResponseHeader(string Name, string Value);
