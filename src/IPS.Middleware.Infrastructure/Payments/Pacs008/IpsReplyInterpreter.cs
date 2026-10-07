using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Payments.Pain002;
using IPS.Middleware.Infrastructure.Transport;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008;

// A final outcome requires HTTP 200 with a schema-valid pacs.002 signed by a trusted IPS certificate that references
// the sent identifiers and reports agreeing final statuses. Everything else is unresolved and needs investigation.
public sealed class IpsReplyInterpreter(IpsSignatureTrust trust) : IIpsReplyInterpreter
{
    internal const string RequestStatusHeader = IpsHeaders.RequestStatus;
    private static readonly XNamespace P = Pacs008Schema.ReplyNamespace;
    private static readonly string[] AcceptedStatuses = ["ACCP", "ACTC", "ACSC"];
    private const string Rejected = "RJCT";

    // A pain.002 is answered by a header, every other message by a signed pacs.002.
    // A reply to our own send is judged when it is interpreted.
    public IpsReply Interpret(IpsSubmissionResponse response, IpsReplyCorrelation sent) =>
        Interpret(response, sent, receivedAtUtc: null);

    // An unsolicited report is judged as of its receipt.
    internal IpsReply Interpret(IpsSubmissionResponse response, IpsReplyCorrelation sent, DateTimeOffset? receivedAtUtc) =>
        sent.MessageDefinition == PaymentMessageTypes.Pain002Definition
            ? Pain002ReplyInterpreter.Interpret(response)
            : Interpret(response, sent, sent.MessageDefinition, strictEvidence: false, receivedAtUtc);

    internal IpsReply Interpret(IpsSubmissionResponse response, IpsReplyCorrelation sent, string messageDefinition) =>
        Interpret(response, sent, messageDefinition, strictEvidence: true, receivedAtUtc: null);

    private IpsReply Interpret(
        IpsSubmissionResponse response,
        IpsReplyCorrelation sent,
        string messageDefinition,
        bool strictEvidence,
        DateTimeOffset? receivedAtUtc)
    {
        var headers = response.Headers
            .Where(header => string.Equals(header.Name, RequestStatusHeader, StringComparison.OrdinalIgnoreCase))
            .Select(header => header.Value.Trim())
            .Distinct()
            .ToArray();
        if (headers.Length > 1)
        {
            return Unresolved("IPS returned conflicting request statuses.");
        }

        var requestStatus = headers.SingleOrDefault();
        if (response.HttpStatusCode != 200)
        {
            return Unresolved($"IPS returned HTTP {response.HttpStatusCode} without a pacs.002 outcome (request status: {requestStatus ?? "missing"}).");
        }

        // Annex D 6.3: a processed send returns a pacs.002 body; the header alone is not a final outcome.
        if (string.IsNullOrWhiteSpace(response.Body))
        {
            return Unresolved($"IPS returned no pacs.002 body (request status: {requestStatus ?? "missing"}).");
        }

        XElement report;
        try
        {
            report = Pacs008Schema.ValidateReply(response.Body).Root!.Element(P + "Document")!.Element(P + "FIToFIPmtStsRpt")!;
        }
        catch (Exception exception) when (exception is XmlException or XmlSchemaException)
        {
            return Unresolved($"The IPS reply is not a valid pacs.002: {exception.Message}");
        }

        var signature = receivedAtUtc is { } at ? trust.Check(response.Body, at) : trust.Check(response.Body);
        if (signature is IpsSignatureCheck.OutsideValidity outside)
        {
            return Unresolved($"The IPS reply was signed by a trusted IPS certificate outside its validity period: {outside.Detail}");
        }

        if (signature is not IpsSignatureCheck.Trusted)
        {
            return Unresolved("The IPS reply signature is missing, invalid or not from a trusted IPS certificate.");
        }

        return InterpretTrustedReport(report, sent, messageDefinition, requestStatus, strictEvidence);
    }

    private static IpsReply InterpretTrustedReport(
        XElement report,
        IpsReplyCorrelation sent,
        string messageDefinition,
        string? requestStatus,
        bool strictEvidence)
    {
        var groups = report.Elements(P + "OrgnlGrpInfAndSts").ToArray();
        var transactions = report.Elements(P + "TxInfAndSts").ToArray();
        if (groups.Length != 1 || transactions.Length > 1)
        {
            return Unresolved("The IPS reply does not follow the single-payment pacs.002 profile.");
        }

        var group = groups[0];
        var transaction = transactions.SingleOrDefault();
        if (!ReferencesSentMessage(group, transaction, sent, messageDefinition))
        {
            return Unresolved("The IPS reply does not reference this payment.");
        }

        if (strictEvidence && HasConflictingNestedReference(transaction, sent, messageDefinition))
        {
            return Unresolved("The IPS reply contains conflicting original message references.");
        }

        var statuses = new[] { Value(group, "GrpSts"), Value(transaction, "TxSts") }.OfType<string>().ToArray();
        if (statuses.Length == 0)
        {
            return Unresolved("The IPS reply contains no payment status.");
        }

        if (statuses.Any(status => Classify(status) is null))
        {
            return Unresolved($"IPS did not return a final status (status: {string.Join(", ", statuses)}).");
        }

        var outcome = Classify(statuses[0])!.Value;
        if (!StatusesAgree(statuses, requestStatus, outcome))
        {
            return Unresolved($"IPS returned conflicting statuses (body: {string.Join(", ", statuses)}; request status: {requestStatus}).");
        }

        // Source mapping: the first reason code and additional information in document order.
        var reasons = report.Descendants(P + "StsRsnInf").ToArray();
        var reasonCodes = ReasonCodes(reasons);
        if (strictEvidence && reasonCodes.Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
        {
            return Unresolved("The IPS reply contains conflicting reason codes.");
        }

        // The short name of the definition, for example pacs.008 from pacs.008.001.12.
        var messageName = string.Join('.', messageDefinition.Split('.')[..2]);
        var description = reasons
            .Elements(P + "AddtlInf")
            .Select(element => element.Value.Trim())
            .FirstOrDefault(text => text.Length > 0);
        if (outcome == IpsReplyStatus.Accepted)
        {
            return new IpsReply(outcome, new PaymentDetails(description: description ?? $"IPS accepted the {messageName}."));
        }

        var details = new PaymentDetails(
            reasonCodes.FirstOrDefault() ?? "NARR",
            InternalCode(requestStatus),
            description ?? $"IPS rejected the {messageName}.");
        return new IpsReply(outcome, details);
    }

    private static bool ReferencesSentMessage(XElement group, XElement? transaction, IpsReplyCorrelation sent, string messageDefinition)
    {
        var groupMatches = Value(group, "OrgnlMsgId") == sent.MessageId
            && string.Equals(Value(group, "OrgnlMsgNmId"), messageDefinition, StringComparison.Ordinal);
        var transactionMatches = transaction is null
            || (Value(transaction, "OrgnlTxId") == sent.TransactionId && Value(transaction, "OrgnlEndToEndId") == sent.EndToEndId);
        return groupMatches && transactionMatches;
    }

    private static bool HasConflictingNestedReference(XElement? transaction, IpsReplyCorrelation sent, string messageDefinition) =>
        transaction?.Element(P + "OrgnlGrpInf") is { } nested
        && (Value(nested, "OrgnlMsgId") != sent.MessageId || Value(nested, "OrgnlMsgNmId") != messageDefinition);

    // Every body status, and the request-status header when present, must report the same outcome.
    private static bool StatusesAgree(string[] statuses, string? requestStatus, IpsReplyStatus outcome) =>
        statuses.All(status => Classify(status) == outcome)
        && (requestStatus is null || Classify(requestStatus.Split('/')[0]) == outcome);

    private static string[] ReasonCodes(XElement[] reasons) =>
        reasons
            .Elements(P + "Rsn")
            .Elements()
            .Where(code => code.Name == P + "Cd" || code.Name == P + "Prtry")
            .Select(code => code.Value.Trim())
            .Where(code => code.Length > 0)
            .ToArray();

    private static IpsReplyStatus? Classify(string status) => status.Trim().ToUpperInvariant() switch
    {
        var code when AcceptedStatuses.Contains(code) => IpsReplyStatus.Accepted,
        Rejected => IpsReplyStatus.Rejected,
        _ => null
    };

    // X-MONTRAN-IPS-ReqSts: RJCT/<IPS error code>.
    private static int? InternalCode(string? requestStatus) =>
        requestStatus?.Split('/') is [_, var code] && int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static string? Value(XElement? parent, string name) => parent?.Element(P + name)?.Value.Trim();

    private static IpsReply Unresolved(string description) => new(IpsReplyStatus.Unresolved, new PaymentDetails(description: description));
}
