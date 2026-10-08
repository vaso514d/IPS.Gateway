using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Camt029;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Payments.Camt029;

public sealed class Camt029Preparation(Pacs008MessageSigner signer, ISigningCertificateSource certificates) : IOutgoingMessageProtocol
{
    public string MessageType => PaymentMessageTypes.Camt029;

    public string BuildUnsignedXml(IAcceptedPayment accepted, string messageId, string transactionId)
    {
        // The protocol is chosen by the payment's message type, so the snapshot is always this type's.
        var payment = (AcceptedCamt029)accepted;
        return new Camt029Xml().Build(payment, new(messageId, transactionId, payment.EnvelopeCreatedAtUtc));
    }

    public Task<SigningResult> SignAsync(string unsignedXml, CancellationToken cancellationToken) =>
        MessageSigning.PrepareAsync(unsignedXml, certificates, signer.PrepareCamt029, cancellationToken);
}
