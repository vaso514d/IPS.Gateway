using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Inbound.Pacs008;

namespace IPS.Middleware.Application.Inbound.Replies;

public interface IIncomingReplyProtocol
{
    // The signature is judged as of the receipt time, so every step that reads the receipt reaches the same verdict.
    IncomingPacs008ReadResult Read(string xml, DateTimeOffset receivedAtUtc);
    string Build(IncomingReplyEnvelope envelope);
    Task<SigningResult> SignAsync(string unsignedXml, CancellationToken cancellationToken);
    ReplyDeliveryResult Interpret(ReplyAttemptCompletion completion, IncomingReplyEnvelope envelope);
}
