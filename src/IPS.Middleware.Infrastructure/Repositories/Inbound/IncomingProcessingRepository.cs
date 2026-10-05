using IPS.Middleware.Application.Abstractions.Persistence;
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
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class IncomingProcessingRepository(TransactionDbContext db) : IIncomingProcessingRepository
{
    public async Task<IncomingProcessingSnapshot?> ReadAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        var row = await db.IncomingMetadata.Include(p => p.Payment).SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var calls = await db.IncomingCoreCalls.Where(c => c.PaymentId == paymentId).OrderBy(c => c.Number).ToListAsync(cancellationToken);
        return new(row.Payment, IncomingPacs008.Freeze(IncomingPaymentJson.Read<Pacs008Request>(row.RequestJson)),
            IncomingPaymentJson.Read<IncomingProcessingContext>(row.ContextJson), calls.Select(c => c.Snapshot()).ToArray(), row.FollowUpAtUtc, row.ReconciliationDeadlineUtc);
    }

    public Task<bool> IsOwnerAsync(IncomingPaymentClaim claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        return db.IncomingMetadata.AsNoTracking().AnyAsync(p => p.Id == claim.PaymentId &&
            p.ClaimToken == claim.Token && p.ClaimExpiresAtUtc > now, cancellationToken);
    }

    public async Task<IncomingCoreCall> StageCallAsync(
        IncomingPaymentClaim claim,
        CoreCallKind kind,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        ReversalNotification? notification = null)
    {
        var payment = await TouchAsync(claim, now, cancellationToken);
        var hasSubmission = await db.IncomingCoreCalls.AnyAsync(c => c.PaymentId == payment.Id && c.Kind == CoreCallKind.Submission, cancellationToken);
        var allowed = kind switch
        {
            CoreCallKind.Submission => payment.IpsAccepted is null && !hasSubmission,
            CoreCallKind.Status => payment.IpsAccepted is null && hasSubmission,
            CoreCallKind.Reconciliation => payment.IpsAccepted == false && hasSubmission && payment.FollowUp == IncomingFollowUp.ReconciliationRequired,
            CoreCallKind.Reversal => payment.IpsAccepted == false && payment.CoreStatus == CoreOutcome.Accepted &&
                payment.FollowUp == IncomingFollowUp.ReversalRequired && payment.Reversal == ReversalDelivery.Started &&
                !await db.IncomingCoreCalls.AnyAsync(c => c.PaymentId == payment.Id && c.Kind == CoreCallKind.Reversal, cancellationToken),
            _ => false
        };
        if (!allowed || (kind == CoreCallKind.Reversal) != (notification is not null))
        {
            throw new InvalidOperationException("The call must match the payment's current processing or follow-up obligation.");
        }

        if (notification is not null)
        {
            var context = IncomingPaymentJson.Read<IncomingProcessingContext>(db.Metadata(payment).ContextJson);
            var expected = new ReversalNotification(payment.Id, payment.ParticipantBic, payment.EndToEndId, payment.IpsDecidedAtUtc!.Value,
                payment.IpsReasonCode, payment.IpsDescription, context.Original.GroupMessageId);
            if (notification != expected)
            {
                throw new InvalidOperationException("Reversal notification must preserve the immutable IPS decision and references.");
            }
        }
        var number = (await db.IncomingCoreCalls.Where(c => c.PaymentId == payment.Id).MaxAsync(c => (int?)c.Number, cancellationToken) ?? 0) + 1;
        var row = new IncomingCoreCallRow
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Number = number,
            Kind = kind,
            OwnerToken = claim.Token,
            RequestJson = notification is null ? null : IncomingPaymentJson.Write(notification),
            StartedAtUtc = now.ToUniversalTime()
        };
        db.IncomingCoreCalls.Add(row);
        db.Changes.AuthorizedIncomingCalls.Add(row.Id);
        return row.Snapshot();
    }

    public async Task StageCompletionAsync(
        IncomingPaymentClaim claim,
        Guid callId,
        CoreCallCompletion completion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await TouchAsync(claim, now, cancellationToken);
        var row = await CallAsync(claim, callId, cancellationToken);
        if (db.Entry(row).State == EntityState.Added || row.OwnerToken != claim.Token || row.CompletionJson is not null ||
            (completion.Response is null) == (completion.Failure is null))
        {
            throw new InvalidOperationException("A committed call accepts one response or failure from its original live owner.");
        }

        row.CompletionJson = IncomingPaymentJson.Write(completion);
        db.Changes.AuthorizedIncomingCalls.Add(row.Id);
    }

    public async Task StageConsumptionAsync(IncomingPaymentClaim claim, Guid callId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await TouchAsync(claim, now, cancellationToken);
        var row = await CallAsync(claim, callId, cancellationToken);
        if (row.CompletionJson is null || db.Entry(row).Property(c => c.CompletionJson).IsModified)
        {
            throw new InvalidOperationException("Interpret only committed call results.");
        }

        row.Consumed = true;
        db.Changes.AuthorizedIncomingCalls.Add(row.Id);
    }

    public async Task StageFinishAsync(
        IncomingPaymentClaim claim,
        DateTimeOffset now,
        DateTimeOffset? followUpAtUtc,
        DateTimeOffset? reconciliationDeadlineUtc,
        CancellationToken cancellationToken)
    {
        var payment = await TouchAsync(claim, now, cancellationToken);
        if (payment.IpsAccepted is null || (payment.FollowUp == IncomingFollowUp.None) != (followUpAtUtc is null) ||
            (followUpAtUtc is null) != (reconciliationDeadlineUtc is null) ||
            reconciliationDeadlineUtc <= payment.IpsDecidedAtUtc || followUpAtUtc > reconciliationDeadlineUtc)
        {
            throw new InvalidOperationException("A final decision and its follow-up obligation must be stored together.");
        }

        var entry = db.Entry(db.Metadata(payment));
        var deadline = entry.Property(p => p.ReconciliationDeadlineUtc);
        if (deadline.CurrentValue is not null && deadline.CurrentValue != reconciliationDeadlineUtc)
        {
            throw new InvalidOperationException("The reconciliation deadline cannot change.");
        }

        deadline.CurrentValue = reconciliationDeadlineUtc?.ToUniversalTime();
        entry.Entity.FollowUpAtUtc = followUpAtUtc?.ToUniversalTime();
        entry.Entity.NextActionAtUtc = null;
        entry.Entity.SetClaim(null, null);
    }

    internal async Task<IncomingPayment> TouchAsync(IncomingPaymentClaim claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        var metadata = await db.IncomingMetadata.Include(p => p.Payment).SingleOrDefaultAsync(p => p.Id == claim.PaymentId, cancellationToken)
            ?? throw new PersistenceConcurrencyException("The incoming payment no longer exists.");
        var payment = metadata.Payment;
        var entry = db.Entry(metadata);
        if (entry.State == EntityState.Added || entry.Property(p => p.ClaimToken).IsModified || !entry.Entity.HasLiveClaim(claim, now))
        {
            throw new PersistenceConcurrencyException("The incoming payment owner is stale or expired.");
        }

        var revision = entry.Property(p => p.CheckpointVersion);
        revision.CurrentValue = checked(revision.CurrentValue + 1);
        db.Changes.AuthorizedIncomingProcessing.Add(payment.Id);
        db.Changes.AuthorizedIncomingPaymentWork.Add(payment.Id);
        return payment;
    }

    private async Task<IncomingCoreCallRow> CallAsync(IncomingPaymentClaim claim, Guid id, CancellationToken cancellationToken) =>
        await db.IncomingCoreCalls.SingleOrDefaultAsync(c => c.Id == id && c.PaymentId == claim.PaymentId, cancellationToken)
            ?? throw new InvalidOperationException("The CBS call belongs to another payment.");
}
