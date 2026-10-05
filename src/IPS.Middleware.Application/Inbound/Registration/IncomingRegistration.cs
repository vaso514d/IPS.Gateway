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

/// <summary>For a conflict, PaymentId is the unchanged canonical payment.</summary>
public sealed class IncomingRegistration
{
    public IncomingRegistration(IncomingRegistrationOutcome outcome, Guid? paymentId)
    {
        Outcome = outcome;
        PaymentId = paymentId;
    }

    public IncomingRegistrationOutcome Outcome { get; init; }
    public Guid? PaymentId { get; init; }

    public static readonly IncomingRegistration LostOwnership = new(IncomingRegistrationOutcome.LostOwnership, null);
}

public sealed class RegisteredIncomingPayment
{
    public RegisteredIncomingPayment(IncomingPayment payment, Pacs008Request request)
    {
        Payment = payment;
        Request = request;
    }

    public IncomingPayment Payment { get; init; }
    public Pacs008Request Request { get; init; }
}
