using IPS.Middleware.Application.Abstractions.Inbound;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using static IPS.Middleware.Infrastructure.Persistence.IncomingProcessingColumns;
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
            FollowUp = EF.Property<DateTimeOffset?>(p, FollowUpAtUtc)
        }).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        var calls = await db.IncomingCoreCalls.Where(c => c.PaymentId == paymentId).OrderBy(c => c.Number).ToListAsync(cancellationToken);
        return new(row.Payment, IncomingPacs008.Freeze(IncomingPaymentJson.Read<Pacs008Request>(row.Request)),
            IncomingPaymentJson.Read<IncomingProcessingContext>(row.Context), calls.Select(c => c.Snapshot()).ToArray(), row.FollowUp);
    }

    public Task<bool> IsOwnerAsync(IncomingPaymentClaim claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        return db.IncomingPayments.AsNoTracking().AnyAsync(p => p.Id == claim.PaymentId &&
            EF.Property<Guid?>(p, ClaimToken) == claim.Token && EF.Property<DateTimeOffset?>(p, ClaimExpiresAtUtc) > now, cancellationToken);
    }

    public async Task<IncomingCoreCall> StageCallAsync(IncomingPaymentClaim claim, CoreCallKind kind, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var payment = await TouchAsync(claim, now, cancellationToken);
        if (payment.IpsAccepted is not null) throw new InvalidOperationException("An IPS decision ends initial CBS processing.");
        var hasSubmission = await db.IncomingCoreCalls.AnyAsync(c => c.PaymentId == payment.Id && c.Kind == CoreCallKind.Submission, cancellationToken);
        if (!Enum.IsDefined(kind) || (kind == CoreCallKind.Submission ? hasSubmission : !hasSubmission))
            throw new InvalidOperationException("Submit once; status queries require a committed submission marker.");
        var number = (await db.IncomingCoreCalls.Where(c => c.PaymentId == payment.Id).MaxAsync(c => (int?)c.Number, cancellationToken) ?? 0) + 1;
        var row = new IncomingCoreCallRow
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Number = number,
            Kind = kind,
            OwnerToken = claim.Token,
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

    public async Task StageFinishAsync(IncomingPaymentClaim claim, DateTimeOffset now, DateTimeOffset? followUpAtUtc, CancellationToken cancellationToken)
    {
        var payment = await TouchAsync(claim, now, cancellationToken);
        if (payment.IpsAccepted is null || (payment.FollowUp == IncomingFollowUp.None) != (followUpAtUtc is null))
            throw new InvalidOperationException("A final decision and its follow-up obligation must be stored together.");
        var entry = db.Entry(payment);
        entry.Property<DateTimeOffset?>(FollowUpAtUtc).CurrentValue = followUpAtUtc?.ToUniversalTime();
        entry.Property<DateTimeOffset?>(NextActionAtUtc).CurrentValue = null;
        entry.Property<Guid?>(ClaimToken).CurrentValue = null;
        entry.Property<DateTimeOffset?>(ClaimExpiresAtUtc).CurrentValue = null;
    }

    private async Task<IncomingPayment> TouchAsync(IncomingPaymentClaim claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        var payment = await db.IncomingPayments.FindAsync([claim.PaymentId], cancellationToken)
            ?? throw new PersistenceConcurrencyException("The incoming payment no longer exists.");
        var entry = db.Entry(payment);
        if (entry.State == EntityState.Added || entry.Property<Guid?>(ClaimToken).IsModified ||
            entry.Property<Guid?>(ClaimToken).CurrentValue != claim.Token ||
            entry.Property<DateTimeOffset?>(ClaimExpiresAtUtc).CurrentValue is not { } expiry || expiry <= now)
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
