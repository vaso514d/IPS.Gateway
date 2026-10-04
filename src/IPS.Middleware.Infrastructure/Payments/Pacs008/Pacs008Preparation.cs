using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008;

public sealed class Pacs008Preparation(Pacs008MessageSigner signer, ISigningCertificateSource certificates) : IPacs008MessagePreparation
{
    public string BuildUnsignedXml(AcceptedPacs008 accepted, string messageId, string transactionId) =>
        new Pacs008Xml(accepted.Profile).Build(accepted.Payment, new(messageId, transactionId, accepted.EnvelopeCreatedAtUtc));

    public async Task<SigningResult> SignAsync(string unsignedXml, CancellationToken cancellationToken)
    {
        X509Certificate2? certificate;
        try { certificate = await certificates.GetCurrentAsync(cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new SigningDeferred($"The signing certificate is unavailable: {exception.Message}");
        }
        try
        {
            var result = signer.Prepare(unsignedXml, certificate);
            return new SignedMessage(result.Xml, result.IsSigned ? SubmissionMessageKind.Signed : SubmissionMessageKind.DevelopmentUnsigned);
        }
        // Certificate problems and an unreachable private key (store, HSM or ACL) can be fixed without changing the payment.
        catch (Exception exception) when (exception is SigningCertificateException or CryptographicException)
        {
            return new SigningDeferred(exception.Message);
        }
    }
}
