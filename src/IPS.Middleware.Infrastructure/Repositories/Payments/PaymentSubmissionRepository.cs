using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Repositories.Payments;

public sealed class PaymentSubmissionRepository(TransactionDbContext db) : IPaymentSubmissionRepository
{
    public async Task<PaymentSubmission?> ReadAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        var stored = await db.Payments.AsNoTracking()
            .Where(p => p.Id == paymentId && p.MessageType == Pacs008)
            .Select(p => new { Marker = EF.Property<string?>(p, SubmissionJson), Response = EF.Property<string?>(p, SubmissionResponseJson) })
            .SingleOrDefaultAsync(cancellationToken);
        return stored is null ? null : new(PaymentJson.Read<SubmissionMarker>(stored.Marker), PaymentJson.Read<IpsSubmissionResponse>(stored.Response));
    }

    public void StageSubmission(OutgoingPayment payment, TransactionClaim claim, SubmissionMessageKind messageKind, DateTimeOffset now)
    {
        if (!Enum.IsDefined(messageKind)) throw new ArgumentOutOfRangeException(nameof(messageKind));
        var entry = db.OwnedPacs008(payment, claim, now);
        if (entry.TextOf(SubmissionJson).CurrentValue is not null)
            throw new InvalidOperationException("Initial submission has already started; inspect the stored response or investigate.");
        var artifact = messageKind == SubmissionMessageKind.Signed ? SignedXml : UnsignedXml;
        if (entry.TextOf(artifact).OriginalValue is null)
            throw new InvalidOperationException("Commit the selected message artifact before starting submission.");
        if (messageKind == SubmissionMessageKind.DevelopmentUnsigned && entry.TextOf(SignedXml).CurrentValue is not null)
            throw new InvalidOperationException("A signed message cannot be downgraded to unsigned submission.");
        db.WriteOnce(entry, SubmissionJson, PaymentJson.Write(new SubmissionMarker(now.ToUniversalTime(), claim.Token, messageKind)));
    }

    public void StageResponse(OutgoingPayment payment, TransactionClaim claim, IpsSubmissionResponse response, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(response);
        var entry = db.OwnedPacs008(payment, claim, now);
        var marker = PaymentJson.Read<SubmissionMarker>(entry.TextOf(SubmissionJson).OriginalValue)
            ?? throw new InvalidOperationException("Commit submission before recording its response.");
        if (marker.ClaimToken != claim.Token)
            throw new PersistenceConcurrencyException("The response belongs to a different submission owner.");
        db.WriteOnce(entry, SubmissionResponseJson, PaymentJson.Write(response));
    }
}
