namespace IPS.Middleware.Infrastructure.Transport;

// One loaded certificate and its validity period, for readiness. Only the subject name is kept.
public sealed record CertificateExpiry(string Source, string Subject, DateTimeOffset NotAfterUtc, DateTimeOffset NotBeforeUtc);

// Implemented by the certificate owners that readiness inspects.
public interface ICertificateExpirySource
{
    IReadOnlyList<CertificateExpiry> Expiries();
}
