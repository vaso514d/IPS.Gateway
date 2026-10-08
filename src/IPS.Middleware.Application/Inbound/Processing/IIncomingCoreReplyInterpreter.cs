using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Processing;

public interface IIncomingCoreReplyInterpreter
{
    CorePaymentResult Interpret(CoreCallCompletion completion, IncomingPacs008Reference original);
}
