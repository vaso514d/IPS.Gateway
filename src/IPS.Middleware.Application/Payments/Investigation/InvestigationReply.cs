using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.Investigation;

public enum InvestigationOutcome { OriginalAccepted, OriginalRejected, NotFound, Unresolved }

/// <summary>Meaning of investigation evidence; NotFound is not itself permission to send a payment.</summary>
public sealed record InvestigationReply(InvestigationOutcome Outcome, PaymentDetails Details);
