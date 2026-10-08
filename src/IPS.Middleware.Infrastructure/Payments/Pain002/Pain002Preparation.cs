using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pain002;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Payments.Pain002;

public sealed class Pain002Preparation(Pacs008MessageSigner signer, ISigningCertificateSource certificates) : IOutgoingMessageProtocol
{
    public string MessageType => PaymentMessageTypes.Pain002;

    public string BuildUnsignedXml(IAcceptedPayment accepted, string messageId, string transactionId)
    {
        // The protocol is chosen by the payment's message type, so the snapshot is always this type's.
        var payment = (AcceptedPain002)accepted;
        return new Pain002Xml().Build(payment, new(messageId, transactionId, payment.EnvelopeCreatedAtUtc));
    }

    public Task<SigningResult> SignAsync(string unsignedXml, CancellationToken cancellationToken) =>
        MessageSigning.PrepareAsync(unsignedXml, certificates, signer.PreparePain002, cancellationToken);
}
