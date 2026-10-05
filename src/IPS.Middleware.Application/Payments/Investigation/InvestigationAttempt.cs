using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Payments.Investigation;

public sealed class InvestigationIdentity
{
    public InvestigationIdentity(
        Guid id,
        int number,
        string messageId,
        string statusRequestId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset deadlineUtc)
    {
        Id = id;
        Number = number;
        MessageId = messageId;
        StatusRequestId = statusRequestId;
        CreatedAtUtc = createdAtUtc;
        DeadlineUtc = deadlineUtc;
    }

    public Guid Id { get; init; }
    public int Number { get; init; }
    public string MessageId { get; init; }
    public string StatusRequestId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset DeadlineUtc { get; init; }
}

public sealed class InvestigationAttempt
{
    public InvestigationAttempt(
        InvestigationIdentity identity,
        string? unsignedXml,
        OutgoingMessage? request,
        OutgoingMessage? response,
        InvestigationReply? result,
        string? transportFailure)
    {
        Identity = identity;
        UnsignedXml = unsignedXml;
        Request = request;
        Response = response;
        Result = result;
        TransportFailure = transportFailure;
    }

    public InvestigationIdentity Identity { get; init; }
    public string? UnsignedXml { get; init; }
    public OutgoingMessage? Request { get; init; }
    public OutgoingMessage? Response { get; init; }
    public InvestigationReply? Result { get; init; }
    public string? TransportFailure { get; init; }
}
