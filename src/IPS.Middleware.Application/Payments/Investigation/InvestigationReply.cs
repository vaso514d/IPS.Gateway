using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.Investigation;

public enum InvestigationOutcome
{
    OriginalAccepted,
    OriginalRejected,
    NotFound,
    Unresolved
}

/// <summary>Meaning of investigation evidence; NotFound is not itself permission to send a payment.</summary>
public sealed class InvestigationReply
{
    public InvestigationReply(InvestigationOutcome outcome, PaymentDetails details)
    {
        Outcome = outcome;
        Details = details;
    }

    public InvestigationOutcome Outcome { get; init; }
    public PaymentDetails Details { get; init; }
}
