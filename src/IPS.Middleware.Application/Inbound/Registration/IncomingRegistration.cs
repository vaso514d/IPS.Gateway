using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Registration;

public enum IncomingRegistrationOutcome
{
    Created,
    Existing,
    Conflict,
    LostOwnership
}

// For a conflict, PaymentId is the unchanged canonical payment.
public sealed record IncomingRegistration(IncomingRegistrationOutcome Outcome, Guid? PaymentId)
{
    public static readonly IncomingRegistration LostOwnership = new(IncomingRegistrationOutcome.LostOwnership, null);
}

public sealed record RegisteredIncomingPayment(IncomingPayment Payment, Pacs008Request Request);
