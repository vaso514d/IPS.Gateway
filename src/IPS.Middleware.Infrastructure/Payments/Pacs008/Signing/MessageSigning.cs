using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

internal static class MessageSigning
{
    internal static async Task<SigningResult> PrepareAsync(
        string xml,
        ISigningCertificateSource certificates,
        Func<string, X509Certificate2?, Pacs008SigningResult> prepare,
        CancellationToken cancellationToken)
    {
        X509Certificate2? certificate;
        try
        {
            certificate = await certificates.GetCurrentAsync(cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return new SigningDeferred($"The signing certificate is unavailable: {error.Message}");
        }
        try
        {
            var result = prepare(xml, certificate);
            return new SignedMessage(result.Xml, result.IsSigned ? SubmissionMessageKind.Signed : SubmissionMessageKind.DevelopmentUnsigned);
        }
        // Key/store failures may recover without changing the already saved message.
        catch (Exception error) when (error is SigningCertificateException or CryptographicException)
        {
            return new SigningDeferred(error.Message);
        }
    }
}
