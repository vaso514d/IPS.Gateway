namespace IPS.Middleware.Application.Payments.Pacs008;

public enum SubmissionMessageKind
{
    Signed,
    DevelopmentUnsigned
}

public sealed class SubmissionMarker
{
    public SubmissionMarker(DateTimeOffset startedAtUtc, Guid claimToken, SubmissionMessageKind messageKind)
    {
        StartedAtUtc = startedAtUtc;
        ClaimToken = claimToken;
        MessageKind = messageKind;
    }

    public DateTimeOffset StartedAtUtc { get; init; }
    public Guid ClaimToken { get; init; }
    public SubmissionMessageKind MessageKind { get; init; }
}

public sealed class PaymentSubmission
{
    public PaymentSubmission(SubmissionMarker? marker, IpsSubmissionResponse? response)
    {
        Marker = marker;
        Response = response;
    }

    public SubmissionMarker? Marker { get; init; }
    public IpsSubmissionResponse? Response { get; init; }
}
