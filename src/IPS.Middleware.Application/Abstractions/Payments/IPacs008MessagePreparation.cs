using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Abstractions.Payments;

public interface IPacs008MessagePreparation
{
    string BuildUnsignedXml(AcceptedPacs008 accepted, string messageId, string transactionId);
    /// <summary>
    /// Sign with the currently available certificate, or return the unsigned XML when the host's Development policy permits.
    /// Defers when no usable certificate or key is available now; nothing has been sent, so the attempt can be repeated.
    /// </summary>
    Task<SigningResult> SignAsync(string unsignedXml, CancellationToken cancellationToken);
}

public abstract class SigningResult
{
}

public sealed class SignedMessage : SigningResult
{
    public SignedMessage(string xml, SubmissionMessageKind kind)
    {
        Xml = xml;
        Kind = kind;
    }

    public string Xml { get; init; }
    public SubmissionMessageKind Kind { get; init; }
}

public sealed class SigningDeferred : SigningResult
{
    public SigningDeferred(string reason)
    {
        Reason = reason;
    }

    public string Reason { get; init; }
}
