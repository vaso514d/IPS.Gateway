using IPS.Middleware.Application.Abstractions.Inbound;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound;

/// <summary>Registers the trusted, valid payment read from an owned receipt.</summary>
public sealed class IncomingPaymentIntake(IIncomingPaymentRepository payments, IInboundWorkRepository receipts,
    IUnitOfWork unitOfWork, TimeProvider timeProvider, IncomingProcessingOptions? options = null)
{
    public const string ConflictReason = "Payment identity conflict: contents differ from the registered payment.";

    /// <summary>
    /// Uniqueness and concurrency failures propagate and fail this scope. A fresh attempt reuses the persisted winner
    /// only when its contents match; otherwise the receipt is held.
    /// </summary>
    public async Task<IncomingRegistration> RegisterAsync(InboundClaim claim, IncomingPacs008 incoming, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (await receipts.FindOwnedAsync(claim, now, cancellationToken) is not { } receipt) return IncomingRegistration.LostOwnership;
        var endToEndId = incoming.Payment.EndToEndId ?? throw new ArgumentException("A read payment has an EndToEndId.", nameof(incoming));
        var existing = await payments.FindAsync(receipt.ParticipantBic, endToEndId, cancellationToken);

        if (!await receipts.StageOriginalReferencesAsync(claim, incoming.Original, now, cancellationToken))
            return IncomingRegistration.LostOwnership;

        // The canonical payment stays unchanged; the conflicting receipt keeps its references for investigation.
        if (existing is not null && !incoming.HasSameContents(existing.Request))
            return await receipts.StageHoldAsync(claim, now, ConflictReason, cancellationToken)
                ? await CommitAsync(IncomingRegistrationOutcome.Conflict, existing.Payment.Id, cancellationToken)
                : IncomingRegistration.LostOwnership;

        var payment = existing?.Payment ?? IncomingPayment.Register(Guid.NewGuid(), receipt.ParticipantBic, endToEndId, now);
        if (!await receipts.StageAttachmentAsync(claim, payment.Id, now, cancellationToken))
            return IncomingRegistration.LostOwnership;
        if (existing is not null) return await CommitAsync(IncomingRegistrationOutcome.Existing, payment.Id, cancellationToken);
        payments.Add(payment, incoming.Payment, new(receipt.JournalId, receipt.ReceivedAtUtc,
            (incoming.Payment.AcceptanceDateTime ?? receipt.ReceivedAtUtc).ToUniversalTime() + (options ?? new()).PaymentWindow,
            incoming.Original));
        return await CommitAsync(IncomingRegistrationOutcome.Created, payment.Id, cancellationToken);
    }

    private async Task<IncomingRegistration> CommitAsync(IncomingRegistrationOutcome outcome, Guid paymentId, CancellationToken cancellationToken)
    {
        await unitOfWork.SaveAsync(cancellationToken);
        return new(outcome, paymentId);
    }
}

public enum IncomingRegistrationOutcome { Created, Existing, Conflict, LostOwnership }

/// <summary>For a conflict, PaymentId is the unchanged canonical payment.</summary>
public sealed record IncomingRegistration(IncomingRegistrationOutcome Outcome, Guid? PaymentId)
{
    public static readonly IncomingRegistration LostOwnership = new(IncomingRegistrationOutcome.LostOwnership, null);
}

public sealed record RegisteredIncomingPayment(IncomingPayment Payment, Pacs008Request Request);
