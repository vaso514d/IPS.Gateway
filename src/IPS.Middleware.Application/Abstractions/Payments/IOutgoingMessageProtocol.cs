using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Abstractions.Payments;

// The only per-type seam of outgoing processing: how one message type is built and signed.
public interface IOutgoingMessageProtocol
{
    string MessageType { get; }

    string BuildUnsignedXml(IAcceptedPayment accepted, string messageId, string transactionId);

    // Signs with the currently available certificate, or returns the unsigned XML when the host's Development policy permits.
    // Defers when no usable certificate or key is available now; nothing has been sent, so the attempt can be repeated.
    Task<SigningResult> SignAsync(string unsignedXml, CancellationToken cancellationToken);
}

public abstract record SigningResult;

public sealed record SignedMessage(string Xml, SubmissionMessageKind Kind) : SigningResult;

public sealed record SigningDeferred(string Reason) : SigningResult;
