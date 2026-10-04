using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Repositories.Payments;

public sealed class PaymentPreparationRepository(TransactionDbContext db) : IPaymentPreparationRepository
{
    public async Task<PreparedPaymentMessage?> ReadAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        var stored = await db.Payments.AsNoTracking()
            .Where(p => p.Id == paymentId && EF.Property<string?>(p, MessageId) != null)
            .Select(p => new
            {
                MessageId = EF.Property<string>(p, MessageId),
                TransactionId = EF.Property<string>(p, ProtocolTransactionId),
                Unsigned = EF.Property<string?>(p, UnsignedXml),
                Signed = EF.Property<string?>(p, SignedXml),
                Accepted = EF.Property<string?>(p, AcceptedJson)
            })
            .SingleOrDefaultAsync(cancellationToken);
        return stored is null ? null : new(stored.MessageId, stored.TransactionId, stored.Unsigned, stored.Signed,
            PaymentJson.ReadAccepted(stored.Accepted));
    }

    public void StageUnsignedXml(OutgoingPayment payment, TransactionClaim claim, string xml, DateTimeOffset now) =>
        Stage(payment, claim, xml, now, UnsignedXml);

    public void StageSignedXml(OutgoingPayment payment, TransactionClaim claim, string xml, DateTimeOffset now) =>
        Stage(payment, claim, xml, now, SignedXml);

    private void Stage(OutgoingPayment payment, TransactionClaim claim, string xml, DateTimeOffset now, string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        var entry = db.OwnedPacs008(payment, claim, now);
        if (column == SignedXml && entry.TextOf(UnsignedXml).OriginalValue is null)
            throw new InvalidOperationException("Commit unsigned XML before staging its signed message.");
        if (entry.TextOf(column).CurrentValue is null && entry.TextOf(SubmissionJson).CurrentValue is not null)
            throw new InvalidOperationException("Preparation cannot change after submission starts.");
        db.WriteOnce(entry, column, xml);
    }
}
