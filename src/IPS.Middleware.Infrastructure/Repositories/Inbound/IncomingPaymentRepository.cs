using IPS.Middleware.Application.Abstractions.Inbound;
using IPS.Middleware.Application.Inbound;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class IncomingPaymentRepository(TransactionDbContext db) : IIncomingPaymentRepository
{
    public async Task<RegisteredIncomingPayment?> FindAsync(string participantBic, string endToEndId, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        var bic = participantBic.Trim().ToUpperInvariant();
        // SQL equality ignores trailing spaces, so the ordinal match is chosen among the padded candidates.
        var candidates = await db.IncomingPayments.Where(p => p.ParticipantBic == bic && p.EndToEndId == endToEndId)
            .Select(p => new { Payment = p, Request = EF.Property<string>(p, RequestJson) }).ToListAsync(cancellationToken);
        return candidates.SingleOrDefault(c => c.Payment.EndToEndId == endToEndId) is { } match
            ? new(match.Payment, IncomingPacs008.Freeze(IncomingPaymentJson.Read<Pacs008Request>(match.Request)))
            : null;
    }

    public void Add(IncomingPayment payment, Pacs008Request request, IncomingProcessingContext context)
    {
        db.RequireUsable();
        if (payment.EventSequence != 1 || payment.PendingEvents.Count != 1)
            throw new ArgumentException("Registration requires a newly registered payment.", nameof(payment));
        if (request.EndToEndId != payment.EndToEndId)
            throw new ArgumentException("The request belongs to another payment.", nameof(request));
        var entry = db.IncomingPayments.Add(payment);
        entry.Property<string>(RequestJson).CurrentValue = IncomingPaymentJson.Write(request);
        entry.Property<string>(IncomingProcessingColumns.ContextJson).CurrentValue = IncomingPaymentJson.Write(context);
        entry.Property<DateTimeOffset?>(NextActionAtUtc).CurrentValue = payment.RegisteredAtUtc;
    }
}
