using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

// CBS calls are journaled before they are made; each completion is written once and consumed once.
public sealed class IncomingProcessingRepository(TransactionDbContext db) : IIncomingProcessingRepository
{
    public async Task<IncomingProcessingSnapshot?> ReadAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var metadata = await db.IncomingMetadata
            .Include(x => x.Payment)
            .SingleOrDefaultAsync(x => x.Id == paymentId, cancellationToken);
        if (metadata is null)
        {
            return null;
        }

        var calls = await db.IncomingCoreCalls
            .Where(x => x.PaymentId == paymentId)
            .OrderBy(x => x.Number)
            .ToListAsync(cancellationToken);

        return new IncomingProcessingSnapshot(
            metadata.Payment,
            IncomingPacs008.Freeze(IncomingPaymentJson.Read<Pacs008Request>(metadata.RequestJson)),
            IncomingPaymentJson.Read<IncomingProcessingContext>(metadata.ContextJson),
            calls.ConvertAll(call => call.Snapshot()),
            metadata.FollowUpAtUtc,
            metadata.ReconciliationDeadlineUtc);
    }

    public Task<bool> IsOwnerAsync(IncomingPaymentClaim claim, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.IncomingMetadata
            .AsNoTracking()
            .AnyAsync(x => x.Id == claim.PaymentId && x.ClaimToken == claim.Token && x.ClaimExpiresAtUtc > now, cancellationToken);

    public async Task<IncomingCoreCall> StageCallAsync(
        IncomingPaymentClaim claim,
        CoreCallKind kind,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        ReversalNotification? notification = null)
    {
        var payment = await db.OwnedIncomingPaymentAsync(claim, now, cancellationToken);
        var expectsNotification = kind == CoreCallKind.Reversal;
        if (!await IsCallAllowedAsync(payment, kind, cancellationToken) || expectsNotification != (notification is not null))
        {
            throw new InvalidOperationException("The call must match the payment's current processing or follow-up obligation.");
        }

        if (notification is not null && notification != FrozenReversalNotification(payment))
        {
            throw new InvalidOperationException("Reversal notification must preserve the immutable IPS decision and references.");
        }

        var lastNumber = await db.IncomingCoreCalls
            .Where(x => x.PaymentId == payment.Id)
            .MaxAsync(x => (int?)x.Number, cancellationToken);
        var call = new IncomingCoreCallRow
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Number = (lastNumber ?? 0) + 1,
            Kind = kind,
            OwnerToken = claim.Token,
            RequestJson = notification is null ? null : IncomingPaymentJson.Write(notification),
            StartedAtUtc = now.ToUniversalTime()
        };
        db.IncomingCoreCalls.Add(call);
        return call.Snapshot();
    }

    public async Task StageCompletionAsync(
        IncomingPaymentClaim claim,
        Guid callId,
        CoreCallCompletion completion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await db.OwnedIncomingPaymentAsync(claim, now, cancellationToken);
        var call = await CallAsync(claim, callId, cancellationToken);
        var completable = db.Entry(call).State != EntityState.Added
            && call.OwnerToken == claim.Token
            && call.CompletionJson is null
            && (completion.Response is null) != (completion.Failure is null);
        if (!completable)
        {
            throw new InvalidOperationException("A committed call accepts one response or failure from its original live owner.");
        }

        call.CompletionJson = IncomingPaymentJson.Write(completion);
    }

    public async Task StageConsumptionAsync(IncomingPaymentClaim claim, Guid callId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.OwnedIncomingPaymentAsync(claim, now, cancellationToken);
        var call = await CallAsync(claim, callId, cancellationToken);
        var completionCommitted = call.CompletionJson is not null && !db.Entry(call).Property(x => x.CompletionJson).IsModified;
        if (!completionCommitted)
        {
            throw new InvalidOperationException("Interpret only committed call results.");
        }

        call.Consumed = true;
    }

    public async Task StageFinishAsync(
        IncomingPaymentClaim claim,
        DateTimeOffset now,
        DateTimeOffset? followUpAtUtc,
        DateTimeOffset? reconciliationDeadlineUtc,
        CancellationToken cancellationToken)
    {
        var payment = await db.OwnedIncomingPaymentAsync(claim, now, cancellationToken);
        if (!IsConsistentFinish(payment, followUpAtUtc, reconciliationDeadlineUtc))
        {
            throw new InvalidOperationException("A final decision and its follow-up obligation must be stored together.");
        }

        var metadata = db.Metadata(payment);
        if (metadata.ReconciliationDeadlineUtc is not null && metadata.ReconciliationDeadlineUtc != reconciliationDeadlineUtc)
        {
            throw new InvalidOperationException("The reconciliation deadline cannot change.");
        }

        metadata.ReconciliationDeadlineUtc = reconciliationDeadlineUtc?.ToUniversalTime();
        metadata.FollowUpAtUtc = followUpAtUtc?.ToUniversalTime();
        metadata.NextActionAtUtc = null;
        metadata.SetClaim(null, null);
    }

    private async Task<bool> IsCallAllowedAsync(IncomingPayment payment, CoreCallKind kind, CancellationToken cancellationToken)
    {
        var submitted = await HasCallAsync(payment.Id, CoreCallKind.Submission, cancellationToken);
        return kind switch
        {
            CoreCallKind.Submission => payment.IpsAccepted is null && !submitted,
            CoreCallKind.Status => payment.IpsAccepted is null && submitted,
            CoreCallKind.Reconciliation => payment.IpsAccepted == false
                && submitted
                && payment.FollowUp == IncomingFollowUp.ReconciliationRequired,
            CoreCallKind.Reversal => payment.IpsAccepted == false
                && payment.CoreStatus == CoreOutcome.Accepted
                && payment.FollowUp == IncomingFollowUp.ReversalRequired
                && payment.Reversal == ReversalDelivery.Started
                && !await HasCallAsync(payment.Id, CoreCallKind.Reversal, cancellationToken),
            _ => false
        };
    }

    private Task<bool> HasCallAsync(Guid paymentId, CoreCallKind kind, CancellationToken cancellationToken) =>
        db.IncomingCoreCalls.AnyAsync(x => x.PaymentId == paymentId && x.Kind == kind, cancellationToken);

    private ReversalNotification FrozenReversalNotification(IncomingPayment payment)
    {
        var context = IncomingPaymentJson.Read<IncomingProcessingContext>(db.Metadata(payment).ContextJson);
        return new ReversalNotification(
            payment.Id,
            payment.ParticipantBic,
            payment.EndToEndId,
            payment.IpsDecidedAtUtc!.Value,
            payment.IpsReasonCode,
            payment.IpsDescription,
            context.Original.GroupMessageId);
    }

    // A follow-up time exists exactly when an obligation exists, and it never passes the reconciliation deadline.
    private static bool IsConsistentFinish(IncomingPayment payment, DateTimeOffset? followUpAtUtc, DateTimeOffset? reconciliationDeadlineUtc)
    {
        var hasObligation = payment.FollowUp != IncomingFollowUp.None;
        return payment.IpsAccepted is not null
            && hasObligation == followUpAtUtc.HasValue
            && followUpAtUtc.HasValue == reconciliationDeadlineUtc.HasValue
            && !(reconciliationDeadlineUtc <= payment.IpsDecidedAtUtc)
            && !(followUpAtUtc > reconciliationDeadlineUtc);
    }

    private async Task<IncomingCoreCallRow> CallAsync(IncomingPaymentClaim claim, Guid callId, CancellationToken cancellationToken) =>
        await db.IncomingCoreCalls.SingleOrDefaultAsync(x => x.Id == callId && x.PaymentId == claim.PaymentId, cancellationToken)
            ?? throw new InvalidOperationException("The CBS call belongs to another payment.");
}
