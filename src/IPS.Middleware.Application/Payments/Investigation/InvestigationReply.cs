using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.Investigation;

public enum InvestigationOutcome
{
    OriginalAccepted,
    OriginalRejected,
    NotFound,
    Unresolved
}

// Meaning of investigation evidence; NotFound is not itself permission to send a payment.
public sealed record InvestigationReply(InvestigationOutcome Outcome, PaymentDetails Details);
