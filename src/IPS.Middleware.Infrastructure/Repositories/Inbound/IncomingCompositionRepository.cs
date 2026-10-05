using IPS.Middleware.Application.Inbound.Composition;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class IncomingCompositionRepository(TransactionDbContext db) : IIncomingCompositionRepository
{
    public async Task<IncomingReceiptState?> ReadAsync(Guid journalId, CancellationToken token)
    {
        db.RequireUsable();
        var row = await db.InboundJournal.AsNoTracking().Where(r => r.Id == journalId)
            .Select(r => new
            {
                r.Status,
                r.NextActionAtUtc,
                r.IncomingPaymentId,
                HasReply = db.IncomingReplies.Any(reply => reply.JournalId == r.Id)
            }).SingleOrDefaultAsync(token);
        if (row is null)
        {
            return null;
        }

        var payment = row.IncomingPaymentId is { } id ? await db.IncomingPayments.AsNoTracking().SingleAsync(p => p.Id == id, token) : null;
        return new(row.Status, row.NextActionAtUtc, row.IncomingPaymentId, row.HasReply, payment?.IpsDecision is not null);
    }

    public async Task<bool> StageFirstReplyReadyAsync(Guid journalId, DateTimeOffset now, CancellationToken token)
    {
        db.RequireUsable();
        var row = await db.InboundJournal.SingleOrDefaultAsync(r => r.Id == journalId, token);
        if (row is null || row.Status != InboundProcessingStatus.Pending ||
            row.ClaimToken is not null && row.ClaimExpiresAtUtc > now)
        {
            return false;
        }
        // Existing reply scheduling belongs exclusively to its delivery workflow, including preparation deferrals.
        if (await db.IncomingReplies.AnyAsync(r => r.JournalId == journalId, token))
        {
            return false;
        }

        if (row.IncomingPaymentId is not { } paymentId)
        {
            return false;
        }

        var payment = await db.IncomingPayments.AsNoTracking().SingleAsync(p => p.Id == paymentId, token);
        if (payment.IpsDecision is null)
        {
            return false;
        }

        row.NextActionAtUtc = now.ToUniversalTime();
        db.Changes.AuthorizedInboundWork.Add(journalId);
        return true;
    }
}
