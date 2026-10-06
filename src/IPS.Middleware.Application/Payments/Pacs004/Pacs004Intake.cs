using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;

namespace IPS.Middleware.Application.Payments.Pacs004;

public sealed class Pacs004Intake(
    IOutgoingPaymentRepository payments,
    OutgoingTransactionIntake intake,
    Pacs008Policy policy,
    Pacs004ProtocolProfile profile,
    TimeProvider timeProvider)
{
    private static readonly IntakeValidationError IdentifierInUse =
        new("id", "The return id is already used by another payment.");

    // A known reference returns its stored payment without validation, so retries stay idempotent after policy changes.
    public async Task<PaymentIntakeResult> AcceptAsync(Pacs004Request request, string requestJson, CancellationToken cancellationToken)
    {
        if (request.ClientReference?.Trim() is { Length: > 0 } reference &&
            await payments.FindByClientReferenceAsync(reference, cancellationToken) is { } existing)
        {
            return new(new(existing, false), []);
        }

        var validation = ValidatedPacs004.Validate(request, policy);
        if (validation.Payment is not { } payment)
        {
            return new(null, validation.Errors);
        }

        // The return id is both protocol ids and must stay unique across payments.
        if (await payments.IsProtocolIdUsedAsync(payment.ReturnId, payment.ReturnId, cancellationToken))
        {
            return new(null, [IdentifierInUse]);
        }

        var envelope = ValidatedIntakeRequest.Validate(PaymentMessageTypes.Pacs004, payment.ClientReference, requestJson);
        if (envelope.Request is not { } stored)
        {
            return new(null, envelope.Errors);
        }

        var now = timeProvider.GetUtcNow();
        try
        {
            return new(await intake.AcceptAsync(stored, now, new AcceptedPacs004(payment, profile, now), cancellationToken), []);
        }
        catch (UniqueConstraintException)
        {
            // A concurrent request took the id between the check and the commit.
            return new(null, [IdentifierInUse]);
        }
    }
}
