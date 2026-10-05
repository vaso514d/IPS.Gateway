namespace IPS.Middleware.Application.Payments.Pacs008;
/// <summary>Normalized payment data and mapping settings fixed at intake; resuming uses these exact values.</summary>
public sealed class AcceptedPacs008
{
    public AcceptedPacs008(
        ValidatedPacs008 payment,
        Pacs008ProtocolProfile profile,
        DateTimeOffset envelopeCreatedAtUtc,
        DateTimeOffset submissionDeadlineUtc)
    {
        Payment = payment;
        Profile = profile;
        EnvelopeCreatedAtUtc = envelopeCreatedAtUtc;
        SubmissionDeadlineUtc = submissionDeadlineUtc;
    }

    public ValidatedPacs008 Payment { get; init; }
    public Pacs008ProtocolProfile Profile { get; init; }
    public DateTimeOffset EnvelopeCreatedAtUtc { get; init; }
    public DateTimeOffset SubmissionDeadlineUtc { get; init; }
}
