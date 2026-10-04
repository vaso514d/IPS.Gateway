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
using static IPS.Middleware.Infrastructure.Persistence.Inbound.IncomingPaymentColumns;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class IncomingProcessingRepository(TransactionDbContext db) : IIncomingProcessingRepository
{
    public async Task<IncomingProcessingSnapshot?> ReadAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        var row = await db.IncomingPayments.Where(p => p.Id == paymentId).Select(p => new
        {
            Payment = p,
            Request = EF.Property<string>(p, RequestJson),
            Context = EF.Property<string>(p, ContextJson),
            Deadline = EF.Property<DateTimeOffset?>(p, ReconciliationDeadlineUtc),
            FollowUp = EF.Property<DateTimeOffset?>(p, FollowUpAtUtc)
        }).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        var calls = await db.IncomingCoreCalls.Where(c => c.PaymentId == paymentId).OrderBy(c => c.Number).ToListAsync(cancellationToken);
        return new(row.Payment, IncomingPacs008.Freeze(IncomingPaymentJson.Read<Pacs008Request>(row.Request)),
            IncomingPaymentJson.Read<IncomingProcessingContext>(row.Context), calls.Select(c => c.Snapshot()).ToArray(), row.FollowUp, row.Deadline);
    }

    public Task<bool> IsOwnerAsync(IncomingPaymentClaim claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        return db.IncomingPayments.AsNoTracking().AnyAsync(p => p.Id == claim.PaymentId &&
            EF.Property<Guid?>(p, ClaimToken) == claim.Token && EF.Property<DateTimeOffset?>(p, ClaimExpiresAtUtc) > now, cancellationToken);
    }

    public async Task<IncomingCoreCall> StageCallAsync(IncomingPaymentClaim claim, CoreCallKind kind, DateTimeOffset now, CancellationToken cancellationToken, ReversalNotification? notification = null)
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
            throw new InvalidOperationException("The call must match the payment's current processing or follow-up obligation.");
        if (notification is not null)
        {
            var context = IncomingPaymentJson.Read<IncomingProcessingContext>(db.Entry(payment).Property<string>(ContextJson).CurrentValue!);
            var expected = new ReversalNotification(payment.Id, payment.ParticipantBic, payment.EndToEndId, payment.IpsDecidedAtUtc!.Value,
                payment.IpsReasonCode, payment.IpsDescription, context.Original.GroupMessageId);
            if (notification != expected) throw new InvalidOperationException("Reversal notification must preserve the immutable IPS decision and references.");
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
        db.AuthorizedIncomingCalls.Add(row.Id);
        return row.Snapshot();
    }

    public async Task StageCompletionAsync(IncomingPaymentClaim claim, Guid callId, CoreCallCompletion completion, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await TouchAsync(claim, now, cancellationToken);
        var row = await CallAsync(claim, callId, cancellationToken);
        if (db.Entry(row).State == EntityState.Added || row.OwnerToken != claim.Token || row.CompletionJson is not null ||
            (completion.Response is null) == (completion.Failure is null))
            throw new InvalidOperationException("A committed call accepts one response or failure from its original live owner.");
        row.CompletionJson = IncomingPaymentJson.Write(completion);
        db.AuthorizedIncomingCalls.Add(row.Id);
    }

    public async Task StageConsumptionAsync(IncomingPaymentClaim claim, Guid callId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await TouchAsync(claim, now, cancellationToken);
        var row = await CallAsync(claim, callId, cancellationToken);
        if (row.CompletionJson is null || db.Entry(row).Property(c => c.CompletionJson).IsModified)
            throw new InvalidOperationException("Interpret only committed call results.");
        row.Consumed = true;
        db.AuthorizedIncomingCalls.Add(row.Id);
    }

    public async Task StageFinishAsync(IncomingPaymentClaim claim, DateTimeOffset now, DateTimeOffset? followUpAtUtc, DateTimeOffset? reconciliationDeadlineUtc, CancellationToken cancellationToken)
    {
        var payment = await TouchAsync(claim, now, cancellationToken);
        if (payment.IpsAccepted is null || (payment.FollowUp == IncomingFollowUp.None) != (followUpAtUtc is null) ||
            (followUpAtUtc is null) != (reconciliationDeadlineUtc is null) ||
            reconciliationDeadlineUtc <= payment.IpsDecidedAtUtc || followUpAtUtc > reconciliationDeadlineUtc)
            throw new InvalidOperationException("A final decision and its follow-up obligation must be stored together.");
        var entry = db.Entry(payment);
        var deadline = entry.Property<DateTimeOffset?>(ReconciliationDeadlineUtc);
        if (deadline.CurrentValue is not null && deadline.CurrentValue != reconciliationDeadlineUtc)
            throw new InvalidOperationException("The reconciliation deadline cannot change.");
        deadline.CurrentValue = reconciliationDeadlineUtc?.ToUniversalTime();
        entry.Property<DateTimeOffset?>(FollowUpAtUtc).CurrentValue = followUpAtUtc?.ToUniversalTime();
        entry.Property<DateTimeOffset?>(NextActionAtUtc).CurrentValue = null;
        entry.SetClaim(null, null);
    }

    internal async Task<IncomingPayment> TouchAsync(IncomingPaymentClaim claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        var payment = await db.IncomingPayments.FindAsync([claim.PaymentId], cancellationToken)
            ?? throw new PersistenceConcurrencyException("The incoming payment no longer exists.");
        var entry = db.Entry(payment);
        if (entry.State == EntityState.Added || entry.Property<Guid?>(ClaimToken).IsModified || !entry.HasLiveClaim(claim, now))
            throw new PersistenceConcurrencyException("The incoming payment owner is stale or expired.");
        var revision = entry.Property<long>(CheckpointVersion);
        revision.CurrentValue = checked(revision.CurrentValue + 1);
        db.AuthorizedIncomingProcessing.Add(payment.Id);
        db.AuthorizedIncomingPaymentWork.Add(payment.Id);
        return payment;
    }

    private async Task<IncomingCoreCallRow> CallAsync(IncomingPaymentClaim claim, Guid id, CancellationToken cancellationToken) =>
        await db.IncomingCoreCalls.SingleOrDefaultAsync(c => c.Id == id && c.PaymentId == claim.PaymentId, cancellationToken)
            ?? throw new InvalidOperationException("The CBS call belongs to another payment.");
}
