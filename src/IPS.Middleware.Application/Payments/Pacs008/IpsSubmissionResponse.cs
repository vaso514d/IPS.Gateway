namespace IPS.Middleware.Application.Payments.Pacs008;

// The received HTTP evidence, before interpreting its business meaning.
public sealed class IpsSubmissionResponse
{
    public IpsSubmissionResponse(int httpStatusCode, string body, IReadOnlyList<IpsResponseHeader> headers)
    {
        if (httpStatusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(httpStatusCode));
        }

        HttpStatusCode = httpStatusCode;
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Headers = Snapshot(headers);
    }

    public int HttpStatusCode { get; }
    public string Body { get; }
    public IReadOnlyList<IpsResponseHeader> Headers { get; }

    // Copy before validating so later changes to the caller's list cannot alter stored evidence.
    private static IReadOnlyList<IpsResponseHeader> Snapshot(IReadOnlyList<IpsResponseHeader> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var snapshot = headers.ToArray();
        if (snapshot.Any(header => header is null || string.IsNullOrWhiteSpace(header.Name) || header.Value is null))
        {
            throw new ArgumentException("Response headers require names and values.", nameof(headers));
        }

        return Array.AsReadOnly(snapshot);
    }
}

public sealed record IpsResponseHeader(string Name, string Value);
