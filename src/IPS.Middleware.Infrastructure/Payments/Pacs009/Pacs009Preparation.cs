using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs009;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Payments.Pacs009;

public sealed class Pacs009Preparation(Pacs008MessageSigner signer, ISigningCertificateSource certificates) : IOutgoingMessageProtocol
{
    public string MessageType => PaymentMessageTypes.Pacs009;

    public string BuildUnsignedXml(IAcceptedPayment accepted, string messageId, string transactionId)
    {
        // The protocol is chosen by the payment's message type, so the snapshot is always this type's.
        var payment = (AcceptedPacs009)accepted;
        return new Pacs009Xml().Build(payment, new(messageId, transactionId, payment.EnvelopeCreatedAtUtc));
    }

    public Task<SigningResult> SignAsync(string unsignedXml, CancellationToken cancellationToken) =>
        MessageSigning.PrepareAsync(unsignedXml, certificates, signer.PreparePacs009, cancellationToken);
}
