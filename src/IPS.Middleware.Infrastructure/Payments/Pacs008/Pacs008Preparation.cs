using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008;

public sealed class Pacs008Preparation(Pacs008MessageSigner signer, ISigningCertificateSource certificates) : IPacs008MessagePreparation
{
    public string BuildUnsignedXml(AcceptedPacs008 accepted, string messageId, string transactionId) =>
        new Pacs008Xml(accepted.Profile).Build(accepted.Payment, new(messageId, transactionId, accepted.EnvelopeCreatedAtUtc));

    public Task<SigningResult> SignAsync(string unsignedXml, CancellationToken cancellationToken) =>
        MessageSigning.PrepareAsync(unsignedXml, certificates, signer.Prepare, cancellationToken);
}
