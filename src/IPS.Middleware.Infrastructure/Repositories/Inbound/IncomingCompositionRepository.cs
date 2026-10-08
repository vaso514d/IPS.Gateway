using IPS.Middleware.Application.Inbound.Composition;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class IncomingCompositionRepository(TransactionDbContext db) : IIncomingCompositionRepository
{
    public async Task<IncomingReceiptState?> ReadAsync(Guid journalId, CancellationToken token)
    {
        var receipt = await db.InboundJournal
            .AsNoTracking()
            .Where(x => x.Id == journalId)
            .Select(x => new
            {
                x.Status,
                x.NextActionAtUtc,
                x.IncomingPaymentId,
                HasReply = db.IncomingReplies.Any(reply => reply.JournalId == x.Id)
            })
            .SingleOrDefaultAsync(token);
        if (receipt is null)
        {
            return null;
        }

        var hasDecision = receipt.IncomingPaymentId is { } paymentId && await HasIpsDecisionAsync(paymentId, token);
        return new IncomingReceiptState(receipt.Status, receipt.NextActionAtUtc, receipt.IncomingPaymentId, receipt.HasReply, hasDecision);
    }

    public async Task<bool> StageFirstReplyReadyAsync(Guid journalId, DateTimeOffset now, CancellationToken token)
    {
        var receipt = await db.InboundJournal.SingleOrDefaultAsync(x => x.Id == journalId, token);
        if (receipt is not { Status: InboundProcessingStatus.Pending } || receipt.ClaimToken is not null && receipt.ClaimExpiresAtUtc > now)
        {
            return false;
        }

        // Existing reply scheduling belongs exclusively to its delivery workflow, including preparation deferrals.
        if (await db.IncomingReplies.AnyAsync(x => x.JournalId == journalId, token))
        {
            return false;
        }

        if (receipt.IncomingPaymentId is not { } paymentId || !await HasIpsDecisionAsync(paymentId, token))
        {
            return false;
        }

        receipt.NextActionAtUtc = now.ToUniversalTime();
        return true;
    }

    private async Task<bool> HasIpsDecisionAsync(Guid paymentId, CancellationToken token)
    {
        var payment = await db.IncomingPayments
            .AsNoTracking()
            .SingleAsync(x => x.Id == paymentId, token);
        return payment.IpsDecision is not null;
    }
}
