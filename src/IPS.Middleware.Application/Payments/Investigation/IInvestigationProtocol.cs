using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Payments.Investigation;

public interface IInvestigationProtocol
{
    string Build(AcceptedPacs008 accepted, IpsReplyCorrelation original, InvestigationIdentity identity);
    Task<SigningResult> SignAsync(string xml, CancellationToken cancellationToken);
    InvestigationReply Interpret(IpsSubmissionResponse response, IpsReplyCorrelation original, string investigationMessageId);
    bool AllowsDevelopmentUnsigned { get; }
}
