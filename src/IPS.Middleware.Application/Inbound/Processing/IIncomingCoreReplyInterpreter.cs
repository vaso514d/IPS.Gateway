using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Processing;

public interface IIncomingCoreReplyInterpreter
{
    // A reply answers the payment only when the identifier it echoes, if any, is the payment's.
    CorePaymentResult Interpret(CoreCallCompletion completion, IncomingPayment payment);
}
