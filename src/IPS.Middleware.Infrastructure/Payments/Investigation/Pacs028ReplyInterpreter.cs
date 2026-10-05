using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Payments.Investigation;
/// <summary>Separates a report about the payment from a rejection of the investigation itself.</summary>
public sealed class Pacs028ReplyInterpreter(IReadOnlyCollection<X509Certificate2> trustedIpsCertificates)
{
    private const int TransactionNotFound = 1016;
    private const int TransactionStillProcessing = 1017;
    private readonly IpsReplyInterpreter _replies = new(trustedIpsCertificates);
    public InvestigationReply Interpret(IpsSubmissionResponse response, IpsReplyCorrelation original, string investigationMessageId)
    {
        var payment = _replies.Interpret(response, original, Pacs008Message.MessageDefinition);
        if (payment.Status == IpsReplyStatus.Accepted)
        {
            return new(InvestigationOutcome.OriginalAccepted, payment.Details);
        }

        if (payment.Status == IpsReplyStatus.Rejected)
        {
            // AG09 cannot distinguish missing from pending. An investigation code is not a payment rejection.
            var ambiguous = payment.Details.ReasonCode == "AG09" ||
                payment.Details.IpsInternalCode is TransactionNotFound or TransactionStillProcessing;
            return new(ambiguous ? InvestigationOutcome.Unresolved : InvestigationOutcome.OriginalRejected, payment.Details);
        }

        var inquiry = _replies.Interpret(response, new IpsReplyCorrelation(original) { MessageId = investigationMessageId }, Pacs028Xml.MessageDefinition);
        return new(inquiry.Status == IpsReplyStatus.Rejected && inquiry.Details.ReasonCode == "AG09" &&
            inquiry.Details.IpsInternalCode == TransactionNotFound
            ? InvestigationOutcome.NotFound : InvestigationOutcome.Unresolved, inquiry.Details);
    }
}
