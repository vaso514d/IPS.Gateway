using System.Security.Cryptography.X509Certificates;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

/// <summary>Supplies the currently configured signing certificate, or null when none is configured. The source owns it.</summary>
public interface ISigningCertificateSource
{
    ValueTask<X509Certificate2?> GetCurrentAsync(CancellationToken cancellationToken);
}
