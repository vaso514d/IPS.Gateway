using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Camt056;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Payments.Camt056;

public sealed class Camt056Preparation(Pacs008MessageSigner signer, ISigningCertificateSource certificates) : IOutgoingMessageProtocol
{
    public string MessageType => PaymentMessageTypes.Camt056;

    public string BuildUnsignedXml(IAcceptedPayment accepted, string messageId, string transactionId)
    {
        // The protocol is chosen by the payment's message type, so the snapshot is always this type's.
        var payment = (AcceptedCamt056)accepted;
        return new Camt056Xml().Build(payment, new(messageId, transactionId, payment.EnvelopeCreatedAtUtc));
    }

    public Task<SigningResult> SignAsync(string unsignedXml, CancellationToken cancellationToken) =>
        MessageSigning.PrepareAsync(unsignedXml, certificates, signer.PrepareCamt056, cancellationToken);
}
