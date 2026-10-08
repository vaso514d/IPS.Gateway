using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Recalls;

namespace IPS.Middleware.Application.Inbound.Transfers;

// The verified content of a recall request (camt.056) another bank sent about a pacs.008 we received. The core system
// answers IPS itself: a pacs.004 accepts the recall, a camt.029 refuses it. The recalled payment is quoted in the shape
// our own recall requests use.
public sealed record IncomingCamt056(
    string MessageId,
    DateTimeOffset CreatedAt,
    string RecallId,
    string? OriginalMessageId,
    string OriginalEndToEndId,
    string OriginalTransactionId,
    decimal OriginalAmount,
    string OriginalCurrency,
    DateOnly OriginalSettlementDate,
    string? ReasonCode,
    RecallOriginalInput Original) : IIncomingTransferContent
{
    string IIncomingTransferContent.Kind => PaymentMessageTypes.Camt056;

    string IIncomingTransferContent.Key => MessageId;

    // The creditor's agent received the recalled payment, so it must be us.
    string IIncomingTransferContent.ReceiverBic => Original.CreditorAgent!.Bic!;

    CoreIdentifiers IIncomingTransferContent.Echo => new(null, MessageId, null);

    public IncomingCamt056 Frozen() => this with { Original = RecallQuote.Frozen(Original) };
}

public static class RecallQuote
{
    // Lists compare by reference, so the address lines are copied into lists that compare by contents. The copy keeps the
    // runtime type, so a camt.029 quote keeps its amount.
    public static T Frozen<T>(T original)
        where T : RecallOriginalInput
    {
        RecallOriginalInput quote = original;
        return (T)(quote with { Debtor = Frozen(quote.Debtor), Creditor = Frozen(quote.Creditor) });
    }

    public static RecallAddressInput? Frozen(RecallAddressInput? address) =>
        address is null ? null : address with { AddressLines = ValueList<string>.Copy(address.AddressLines) };

    private static RecallPartyInput? Frozen(RecallPartyInput? party) =>
        party is null ? null : party with { Address = Frozen(party.Address) };
}
