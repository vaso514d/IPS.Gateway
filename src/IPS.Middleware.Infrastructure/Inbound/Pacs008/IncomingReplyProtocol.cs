using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Inbound.Pacs008;

public sealed class IncomingReplyProtocol(Pacs008MessageSigner signer, ISigningCertificateSource certificates,
    IReadOnlyCollection<X509Certificate2> trustedIpsCertificates) : IIncomingReplyProtocol
{
    private static readonly XNamespace Head = Pacs008Xml.HeaderNamespace;

    public IncomingPacs008ReadResult Read(string xml) => new IncomingPacs008Reader().Read(xml, trustedIpsCertificates);
    public string Build(IncomingReplyEnvelope envelope) => new IncomingPacs002Reply(envelope.Profile, signer)
        .BuildUnsigned(envelope.Original, envelope.Decision, envelope.Context, envelope.ParticipantBic);

    public Task<SigningResult> SignAsync(string unsignedXml, CancellationToken cancellationToken) =>
        MessageSigning.PrepareAsync(unsignedXml, certificates, signer.PrepareReply, cancellationToken);

    public ReplyDeliveryResult Interpret(ReplyAttemptCompletion completion, IncomingReplyEnvelope envelope)
    {
        if (completion.Response is not { } response) return Unresolved(completion.Failure ?? "No IPS response.");
        if (!HasDocumentedRequestStatus(response)) return Unresolved("Missing or unsupported IPS request status.");
        // Annex D: ReplyToPayment returns the final status of the original payment, including on replay.
        var original = envelope.Original;
        var result = new IpsReplyInterpreter(trustedIpsCertificates).Interpret(response,
            new(original.GroupMessageId, original.TransactionId!, original.EndToEndId));
        if (result.Status == IpsReplyStatus.Unresolved) return Unresolved(result.Details.Description ?? "Unresolved IPS reply.");
        if (!MatchesEnvelope(response.Body, envelope))
            return Unresolved("IPS reply envelope does not match the frozen participants or message definition.");
        return (result.Status == IpsReplyStatus.Accepted) == envelope.Decision.Accepted
            ? new(ReplyDeliveryOutcome.Delivered, "IPS confirmed the stored payment decision.")
            : new(ReplyDeliveryOutcome.Conflict, "IPS final status contradicts the immutable local decision.");
    }

    // Annex D 6.3: exactly one ACCP or RJCT/<numeric error code>.
    private static bool HasDocumentedRequestStatus(IpsSubmissionResponse response) =>
        response.Headers.Where(h => h.Name.Equals(IpsReplyInterpreter.RequestStatusHeader, StringComparison.OrdinalIgnoreCase))
            .Select(h => h.Value.Trim()).Distinct(StringComparer.Ordinal).ToArray() is [var status] &&
        (status == "ACCP" || status.StartsWith("RJCT/", StringComparison.Ordinal) &&
            int.TryParse(status.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out _));

    // The reply comes from IPS to the frozen participant and is the pacs.002 version this service sends.
    private static bool MatchesEnvelope(string body, IncomingReplyEnvelope envelope)
    {
        var header = XDocument.Parse(body).Root!.Element(Head + "AppHdr")!;
        string? Party(string name) => header.Element(Head + name)?.Element(Head + "FIId")?.Element(Head + "FinInstnId")?.Element(Head + "BICFI")?.Value;
        return Party("Fr") == envelope.Profile.IpsBic && Party("To") == envelope.ParticipantBic &&
            header.Element(Head + "MsgDefIdr")?.Value == IncomingPacs002Reply.MessageDefinition;
    }

    private static ReplyDeliveryResult Unresolved(string reason) => new(ReplyDeliveryOutcome.Unresolved, reason);
}
