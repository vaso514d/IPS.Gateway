using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Payments;

public sealed class PaymentPreparationRepository(TransactionDbContext db) : IPaymentPreparationRepository
{
    public async Task<PreparedPaymentMessage?> ReadAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        return await db.Payments.AsNoTracking()
            .Where(p => p.Id == paymentId && EF.Property<string?>(p, "MessageId") != null)
            .Select(p => new PreparedPaymentMessage(
                EF.Property<string>(p, "MessageId"), EF.Property<string>(p, "ProtocolTransactionId"),
                EF.Property<string?>(p, "UnsignedXml"), EF.Property<string?>(p, "SignedXml")))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public void StageUnsignedXml(OutgoingPayment payment, TransactionClaim claim, string xml, DateTimeOffset now) =>
        Stage(payment, claim, xml, now, "UnsignedXml");

    public void StageSignedXml(OutgoingPayment payment, TransactionClaim claim, string xml, DateTimeOffset now) =>
        Stage(payment, claim, xml, now, "SignedXml");

    private void Stage(OutgoingPayment payment, TransactionClaim claim, string xml, DateTimeOffset now, string property)
    {
        db.RequireUsable();
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        ArgumentNullException.ThrowIfNull(claim);
        var entry = db.Entry(payment);
        if (entry.State is EntityState.Added or EntityState.Detached ||
            payment.MessageType != "pacs.008" || payment.CurrentStatus != TransactionStatus.Sending ||
            entry.Property<string?>("MessageId").CurrentValue is null)
            throw new InvalidOperationException("Preparation requires a persisted pacs.008 in Sending with stored identifiers.");
        if (!PaymentOwnership.HasLiveClaim(entry, claim, now))
            throw new PersistenceConcurrencyException("Preparation requires the current unexpired claim.");
        if (property == "SignedXml" && entry.Property<string?>("UnsignedXml").OriginalValue is null)
            throw new InvalidOperationException("Commit unsigned XML before staging its signed message.");

        var slot = entry.Property<string?>(property);
        if (slot.CurrentValue is { } existing)
        {
            if (!string.Equals(existing, xml, StringComparison.Ordinal))
                throw new InvalidOperationException("Stored preparation artifacts cannot be replaced.");
            return;
        }
        slot.CurrentValue = xml;
        db.AuthorizedOwnership.Add(payment.Id);
        db.AuthorizedPreparation.Add((payment.Id, property), xml);
    }
}
