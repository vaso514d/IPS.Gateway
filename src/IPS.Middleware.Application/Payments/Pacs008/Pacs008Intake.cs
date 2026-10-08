using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Transactions;

namespace IPS.Middleware.Application.Payments.Pacs008;

public sealed class Pacs008Intake(
        IOutgoingPaymentRepository payments,
        OutgoingTransactionIntake intake,
        Pacs008Policy policy,
        Pacs008ProtocolProfile profile,
        Pacs008Options options,
        TimeProvider timeProvider)
{
    // A known reference returns its stored payment without validation, so retries stay idempotent after policy changes.
    // New requests are validated against current policy and stored with the snapshot later processing uses.
    public async Task<PaymentIntakeResult> AcceptAsync(Pacs008Request request, string requestJson, CancellationToken cancellationToken)
    {
        if (request.ClientReference?.Trim() is { Length: > 0 } reference &&
            await payments.FindByClientReferenceAsync(reference, cancellationToken) is { } existing)
        {
            return new(new(existing, false), []);
        }

        var validation = ValidatedPacs008.Validate(request, policy);
        if (validation.Payment is not { } payment)
        {
            return new(null, validation.Errors);
        }

        var envelope = ValidatedIntakeRequest.Validate(PaymentMessageTypes.Pacs008, payment.ClientReference, requestJson);
        if (envelope.Request is not { } stored)
        {
            return new(null, envelope.Errors);
        }

        var now = timeProvider.GetUtcNow();
        var accepted = new AcceptedPacs008(payment, profile, now, payment.AcceptanceDateTime + options.SubmissionWindow);
        return new(await intake.AcceptAsync(stored, now, accepted, cancellationToken), []);
    }
}


