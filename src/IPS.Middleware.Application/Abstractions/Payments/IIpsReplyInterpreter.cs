using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Abstractions.Payments;

public interface IIpsReplyInterpreter
{
    IpsReply Interpret(IpsSubmissionResponse response, IpsReplyCorrelation sent);
}
