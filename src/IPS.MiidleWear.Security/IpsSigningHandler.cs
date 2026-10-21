using System.Security.Cryptography.X509Certificates;

namespace IPS.MiidleWear.Security;

/// <summary>
/// Signs every request of an <see cref="HttpClient"/>: computes the digest of the body, signs it with the certificate and
/// adds the four signature headers. The body is buffered first, so the bytes sent are exactly the bytes signed.
/// </summary>
/// <example><c>services.AddHttpClient("gateway").AddHttpMessageHandler(() => new IpsSigningHandler(certificate, IpsSignatureAlgorithm.RS256));</c></example>
public sealed class IpsSigningHandler : DelegatingHandler
{
    private readonly X509Certificate2 _certificate;
    private readonly IpsSignatureAlgorithm _algorithm;

    /// <summary>Signs with <paramref name="certificate"/>, which must have a private key of the algorithm's type.</summary>
    public IpsSigningHandler(X509Certificate2 certificate, IpsSignatureAlgorithm algorithm)
    {
        if (!certificate.HasPrivateKey)
        {
            throw new ArgumentException($"Certificate {certificate.Thumbprint} has no private key.", nameof(certificate));
        }

        _certificate = certificate;
        _algorithm = algorithm;
    }

    /// <summary>
    /// A handler for configured signing: it signs with the loaded certificate, or passes requests on unsigned when no
    /// certificate is configured.
    /// </summary>
    public static DelegatingHandler Create(IpsSigningSettings? signing) =>
        signing is { IsConfigured: true }
            ? new IpsSigningHandler(signing.Load(), signing.SignatureAlgorithm())
            : new UnsignedHandler();

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await BufferAsync(request, cancellationToken);
        var digest = IpsContentSignature.Digest(body);
        request.Headers.Remove(IpsSecurityHeaders.ContentSha256);
        request.Headers.Remove(IpsSecurityHeaders.Signature);
        request.Headers.Remove(IpsSecurityHeaders.SignatureAlgorithm);
        request.Headers.Remove(IpsSecurityHeaders.Certificate);
        request.Headers.Add(IpsSecurityHeaders.ContentSha256, digest);
        request.Headers.Add(IpsSecurityHeaders.Signature, IpsContentSignature.Sign(digest, _certificate, _algorithm));
        request.Headers.Add(IpsSecurityHeaders.SignatureAlgorithm, IpsContentSignature.Name(_algorithm));
        request.Headers.Add(IpsSecurityHeaders.Certificate, _certificate.Thumbprint);
        return await base.SendAsync(request, cancellationToken);
    }

    // Serializing content (for example JsonContent) can produce the bytes only once; they are kept and sent as signed.
    private static async Task<byte[]> BufferAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            return [];
        }

        var body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var buffered = new ByteArrayContent(body);
        foreach (var header in request.Content.Headers)
        {
            buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        request.Content.Dispose();
        request.Content = buffered;
        return body;
    }

    private sealed class UnsignedHandler : DelegatingHandler;
}
