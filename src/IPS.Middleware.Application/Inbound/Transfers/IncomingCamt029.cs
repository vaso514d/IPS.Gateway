using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Camt029;

namespace IPS.Middleware.Application.Inbound.Transfers;

// The verified content of a creditor bank's refusal (camt.029, RJCR) of a recall we sent. It is delivered only once it is
// matched to that recall, whose client reference it then carries to the core system.
public sealed record IncomingCamt029(
    string MessageId,
    DateTimeOffset CreatedAt,
    string CancellationStatusId,
    string? OriginalMessageId,
    string OriginalEndToEndId,
    string OriginalTransactionId,
    string? ReasonCode,
    string? AdditionalInformation,
    Camt029OriginalInput Original,
    string? RecallClientReference = null) : IIncomingTransferContent
{
    string IIncomingTransferContent.Kind => PaymentMessageTypes.Camt029;

    string IIncomingTransferContent.Key => MessageId;

    // The debtor's agent sent the recalled payment and the recall, so it must be us.
    string IIncomingTransferContent.ReceiverBic => Original.DebtorAgent!.Bic!;

    CoreIdentifiers IIncomingTransferContent.Echo => new(null, MessageId, null);

    public IncomingCamt029 Frozen() => this with { Original = RecallQuote.Frozen(Original) };
}
