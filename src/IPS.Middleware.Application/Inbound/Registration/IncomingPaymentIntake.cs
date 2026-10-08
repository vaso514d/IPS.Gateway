using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Registration;

// Registers the trusted, valid payment read from an owned receipt.
public sealed class IncomingPaymentIntake(
        IIncomingPaymentRepository payments,
        IInboundWorkRepository receipts,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IncomingProcessingOptions options)
{
    public const string ConflictReason = "Payment identity conflict: contents differ from the registered payment.";

    // Uniqueness and concurrency failures propagate and fail this scope. A fresh attempt reuses the persisted winner
    // only when its contents match; otherwise the receipt is held.
    public Task<IncomingRegistration> RegisterAsync(InboundClaim claim, IncomingPacs008 incoming, CancellationToken cancellationToken) =>
        RegisterAsync(claim, incoming, null, cancellationToken);

    public Task<IncomingRegistration> RegisterAndReleaseAsync(
        InboundClaim claim,
        IncomingPacs008 incoming,
        DateTimeOffset nextActionAtUtc,
        CancellationToken cancellationToken) => RegisterAsync(claim, incoming, nextActionAtUtc, cancellationToken);

    private async Task<IncomingRegistration> RegisterAsync(
        InboundClaim claim,
        IncomingPacs008 incoming,
        DateTimeOffset? nextActionAtUtc,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (await receipts.FindOwnedAsync(claim, now, cancellationToken) is not { } receipt)
        {
            return IncomingRegistration.LostOwnership;
        }

        var endToEndId = incoming.Payment.EndToEndId!;
        var existing = await payments.FindAsync(receipt.ParticipantBic, endToEndId, cancellationToken);

        if (!await receipts.StageOriginalReferencesAsync(claim, incoming.Original, now, cancellationToken))
        {
            return IncomingRegistration.LostOwnership;
        }

        // The canonical payment stays unchanged; the conflicting receipt keeps its references for investigation.
        if (existing is not null && !incoming.HasSameContents(existing.Request))
        {
            return await receipts.StageHoldAsync(claim, now, ConflictReason, cancellationToken)
                ? await CommitAsync(IncomingRegistrationOutcome.Conflict, existing.Payment.Id, cancellationToken)
                : IncomingRegistration.LostOwnership;
        }

        var payment = existing?.Payment ?? IncomingPayment.Register(Guid.NewGuid(), receipt.ParticipantBic, endToEndId, now);
        if (!await receipts.StageAttachmentAsync(claim, payment.Id, now, cancellationToken))
        {
            return IncomingRegistration.LostOwnership;
        }

        if (nextActionAtUtc is { } due && !await receipts.StageFinishAsync(claim, now, due, cancellationToken))
        {
            throw new PersistenceConcurrencyException("Receipt ownership expired during registration.");
        }

        if (existing is not null)
        {
            return await CommitAsync(IncomingRegistrationOutcome.Existing, payment.Id, cancellationToken);
        }

        var acceptedAt = (incoming.Payment.AcceptanceDateTime ?? receipt.ReceivedAtUtc).ToUniversalTime();
        var context = new IncomingProcessingContext(receipt.JournalId, receipt.ReceivedAtUtc, acceptedAt + options.PaymentWindow, incoming.Original);
        payments.Add(payment, incoming.Payment, context);
        return await CommitAsync(IncomingRegistrationOutcome.Created, payment.Id, cancellationToken);
    }

    private async Task<IncomingRegistration> CommitAsync(IncomingRegistrationOutcome outcome, Guid paymentId, CancellationToken cancellationToken)
    {
        await unitOfWork.SaveAsync(cancellationToken);
        return new(outcome, paymentId);
    }
}
