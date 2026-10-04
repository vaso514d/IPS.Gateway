namespace IPS.Middleware.Application.Payments.Pacs008;

/// <summary>Normalized payment data and mapping settings fixed at intake; resuming uses these exact values.</summary>
public sealed record AcceptedPacs008(
    ValidatedPacs008 Payment, Pacs008ProtocolProfile Profile, DateTimeOffset EnvelopeCreatedAtUtc, DateTimeOffset SubmissionDeadlineUtc);
