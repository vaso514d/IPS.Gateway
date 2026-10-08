using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008;

public sealed class Pacs008Preparation(Pacs008MessageSigner signer, ISigningCertificateSource certificates) : IOutgoingMessageProtocol
{
    public string MessageType => PaymentMessageTypes.Pacs008;

    public string BuildUnsignedXml(IAcceptedPayment accepted, string messageId, string transactionId)
    {
        // The protocol is chosen by the payment's message type, so the snapshot is always this type's.
        var payment = (AcceptedPacs008)accepted;
        return new Pacs008Xml(payment.Profile).Build(payment.Payment, new(messageId, transactionId, payment.EnvelopeCreatedAtUtc));
    }

    public Task<SigningResult> SignAsync(string unsignedXml, CancellationToken cancellationToken) =>
        MessageSigning.PrepareAsync(unsignedXml, certificates, signer.Prepare, cancellationToken);
}
