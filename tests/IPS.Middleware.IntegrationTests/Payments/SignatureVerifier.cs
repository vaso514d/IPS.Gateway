using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.IntegrationTests.Payments;

// Checks a generated message against the IPS signature profile with one certificate, judged inside its validity period.
internal static class SignatureVerifier
{
    internal static bool Verifies(string xml, X509Certificate2 certificate) =>
        IpsSignatureVerifier.Check(xml, [certificate], new DateTimeOffset(certificate.NotBefore.ToUniversalTime())) is IpsSignatureCheck.Trusted;
}
