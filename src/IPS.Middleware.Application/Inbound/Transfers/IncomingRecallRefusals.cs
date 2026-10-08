using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Camt056;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Inbound.Transfers;

public abstract record RecallRefusalMatch
{
    // The refusal carries the recall's client reference; the recall is tracked in this scope.
    public sealed record Matched(OutgoingPayment Recall, IncomingCamt029 Refusal) : RecallRefusalMatch;

    public sealed record Unmatched(string Reason) : RecallRefusalMatch;
}

// Matches a verified camt.029 refusal to the outgoing camt.056 recall it answers and records the refusal on that recall.
// IPS does not check that a recall answer names a real recall, so nothing is guessed: the refusal must name our recall's
// message id, and its original end-to-end and transaction ids must be those of the payment the recall named.
public sealed class IncomingRecallRefusals(
    IOutgoingPaymentRepository payments,
    IPaymentPreparationRepository preparation,
    ITransactionWorkRepository paymentWork)
{
    public const string NoOriginalGroup = "The refusal has no original group information, so it names no recall of ours.";
    public const string UnknownRecall = "No recall of ours has the original message id of this refusal.";
    public const string RecallDataUnavailable = "Our recall has no accepted content to compare the refusal with.";
    public const string DisagreeingIdentifiers = "The original end-to-end or transaction id of the refusal differs from our recall's.";

    public async Task<RecallRefusalMatch> MatchAsync(IncomingCamt029 refusal, CancellationToken token)
    {
        if (refusal.OriginalMessageId is null)
        {
            return new RecallRefusalMatch.Unmatched(NoOriginalGroup);
        }

        var recall = await payments.FindByMessageIdAsync(refusal.OriginalMessageId, token);
        if (recall is not { IsRecall: true })
        {
            return new RecallRefusalMatch.Unmatched(UnknownRecall);
        }

        var message = await preparation.ReadAsync(recall.Id, token);
        if (message?.Accepted is not AcceptedCamt056 { Payment: var recalled })
        {
            return new RecallRefusalMatch.Unmatched(RecallDataUnavailable);
        }

        if (!NamesRecalledPayment(refusal, recalled))
        {
            return new RecallRefusalMatch.Unmatched(DisagreeingIdentifiers);
        }

        // The core system learns which of its recalls was refused through the recall's client reference.
        return new RecallRefusalMatch.Matched(recall, refusal with { RecallClientReference = recall.ClientReference });
    }

    // Staged with the delivery, so both commit together. Fenced by the recall's row version, without touching its ownership.
    // False while the recall's owner is working on it (an uncertain recall under investigation): the refusal waits for it.
    public bool TryRecord(RecallRefusalMatch.Matched match, DateTimeOffset now)
    {
        if (!paymentWork.StageUnclaimedObservation(match.Recall, now))
        {
            return false;
        }

        var refusal = match.Refusal;
        match.Recall.RecordRecallRefusal(refusal.ReasonCode, refusal.MessageId, refusal.CancellationStatusId, now);
        return true;
    }

    private static bool NamesRecalledPayment(IncomingCamt029 refusal, ValidatedCamt056 recalled) =>
        refusal.OriginalEndToEndId == recalled.OriginalEndToEndId
        && refusal.OriginalTransactionId == recalled.OriginalTransactionId;
}
