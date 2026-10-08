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
        var candidates = await db.IncomingMetadata
            .Include(x => x.Payment)
            .Where(x => x.Payment.ParticipantBic == bic && x.Payment.EndToEndId == endToEndId)
            .ToListAsync(cancellationToken);
        var match = candidates.SingleOrDefault(x => x.Payment.EndToEndId == endToEndId);
        if (match is null)
        {
            return null;
        }

        var request = IncomingPacs008.Freeze(IncomingPaymentJson.Read<Pacs008Request>(match.RequestJson));
        return new RegisteredIncomingPayment(match.Payment, request);
    }

    public void Add(IncomingPayment payment, Pacs008Request request, IncomingProcessingContext context)
    {
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
