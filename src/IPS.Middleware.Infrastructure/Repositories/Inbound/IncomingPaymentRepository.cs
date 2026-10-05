using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class IncomingPaymentRepository(TransactionDbContext db) : IIncomingPaymentRepository
{
    public async Task<RegisteredIncomingPayment?> FindAsync(string participantBic, string endToEndId, CancellationToken cancellationToken)
    {
        var bic = participantBic.Trim().ToUpperInvariant();
        // SQL equality ignores trailing spaces, so the ordinal match is chosen among the padded candidates.
        var candidates = await db.IncomingMetadata.Include(p => p.Payment).Where(p => p.Payment.ParticipantBic == bic && p.Payment.EndToEndId == endToEndId)
            .ToListAsync(cancellationToken);
        return candidates.SingleOrDefault(c => c.Payment.EndToEndId == endToEndId) is { } match
            ? new(match.Payment, IncomingPacs008.Freeze(IncomingPaymentJson.Read<Pacs008Request>(match.RequestJson)))
            : null;
    }

    public void Add(IncomingPayment payment, Pacs008Request request, IncomingProcessingContext context)
    {
        if (payment.EventSequence != 1 || payment.PendingEvents.Count != 1)
        {
            throw new ArgumentException("Registration requires a newly registered payment.", nameof(payment));
        }

        if (request.EndToEndId != payment.EndToEndId)
        {
            throw new ArgumentException("The request belongs to another payment.", nameof(request));
        }

        db.IncomingMetadata.Add(new IncomingPaymentMetadata
        {
            Id = payment.Id,
            Payment = payment,
            RequestJson = IncomingPaymentJson.Write(request),
            ContextJson = IncomingPaymentJson.Write(context),
            NextActionAtUtc = payment.RegisteredAtUtc
        });
    }
}
