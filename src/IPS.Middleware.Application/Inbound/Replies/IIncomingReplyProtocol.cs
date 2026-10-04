using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Inbound.Pacs008;

namespace IPS.Middleware.Application.Inbound.Replies;

public interface IIncomingReplyProtocol
{
    IncomingPacs008ReadResult Read(string xml);
    string Build(IncomingReplyEnvelope envelope);
    Task<SigningResult> SignAsync(string unsignedXml, CancellationToken cancellationToken);
    ReplyDeliveryResult Interpret(ReplyAttemptCompletion completion, IncomingReplyEnvelope envelope);
}
