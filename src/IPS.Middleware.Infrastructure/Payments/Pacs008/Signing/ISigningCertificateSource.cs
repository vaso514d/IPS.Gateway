using System.Security.Cryptography.X509Certificates;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

// Supplies the currently configured signing certificate, or null when none is configured. The source owns it.
public interface ISigningCertificateSource
{
    ValueTask<X509Certificate2?> GetCurrentAsync(CancellationToken cancellationToken);
}
