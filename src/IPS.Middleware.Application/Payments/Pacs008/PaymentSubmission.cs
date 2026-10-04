namespace IPS.Middleware.Application.Payments.Pacs008;

public enum SubmissionMessageKind { Signed, DevelopmentUnsigned }

public sealed record SubmissionMarker(DateTimeOffset StartedAtUtc, Guid ClaimToken, SubmissionMessageKind MessageKind);

public sealed record PaymentSubmission(SubmissionMarker? Marker, IpsSubmissionResponse? Response);
