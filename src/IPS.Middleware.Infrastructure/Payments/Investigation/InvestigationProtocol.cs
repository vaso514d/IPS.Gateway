using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Payments.Investigation;

public sealed class InvestigationProtocol(
        Pacs008MessageSigner signer,
        ISigningCertificateSource certificates,
        Pacs008SigningPolicy policy,
        Pacs028ReplyInterpreter replies) : IInvestigationProtocol
{
    public bool AllowsDevelopmentUnsigned => policy.AllowUnsignedWithoutCertificate;
    public string Build(AcceptedPacs008 accepted, IpsReplyCorrelation original, InvestigationIdentity identity) =>
        new Pacs028Xml(accepted.Profile).Build(accepted.Payment, original, new(identity.MessageId, identity.StatusRequestId, identity.CreatedAtUtc));
    public Task<SigningResult> SignAsync(string xml, CancellationToken cancellationToken) =>
        MessageSigning.PrepareAsync(xml, certificates, signer.PrepareInvestigation, cancellationToken);
    public InvestigationReply Interpret(IpsSubmissionResponse response, IpsReplyCorrelation original, string investigationMessageId) =>
        replies.Interpret(response, original, investigationMessageId);
}
