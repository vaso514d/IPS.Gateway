namespace IPS.Middleware.Application.Payments.Pacs008;

// Normalized payment data and mapping settings fixed at intake; resuming uses these exact values.
public sealed record AcceptedPacs008(
    ValidatedPacs008 Payment,
    Pacs008ProtocolProfile Profile,
    DateTimeOffset EnvelopeCreatedAtUtc,
    DateTimeOffset SubmissionDeadlineUtc);
