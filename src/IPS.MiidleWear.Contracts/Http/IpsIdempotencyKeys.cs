using IPS.MiidleWear.Contracts.Camt029;
using IPS.MiidleWear.Contracts.Camt055;
using IPS.MiidleWear.Contracts.Camt056;
using IPS.MiidleWear.Contracts.Pacs004;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;
using IPS.MiidleWear.Contracts.Pain001;
using IPS.MiidleWear.Contracts.Transactions;

namespace IPS.MiidleWear.Contracts.Http;

/// <summary>HTTP header names of the contract.</summary>
public static class IpsHttpHeaders
{
    /// <summary>
    /// Sent by the gateway on every call to the core system that delivers something. A repeated key is a redelivery
    /// of the same message and must get the first answer; see <see cref="IpsIdempotencyKeys"/>.
    /// </summary>
    public const string IdempotencyKey = "Idempotency-Key";
}

/// <summary>
/// The <see cref="IpsHttpHeaders.IdempotencyKey"/> the gateway sends with each delivery, derived from the delivered
/// message, so a core system can check the header or derive the key itself. The same key is the <c>reference</c> of
/// the payment-status query (<see cref="TransactionRestApiRoutes.PaymentStatus"/>) for the receive kinds.
/// </summary>
public static class IpsIdempotencyKeys
{
    /// <summary>An incoming pacs.008: its <c>transactionId</c>, also the status reference of its own query.</summary>
    public static string? For(IncomingPaymentRequestDto payment) => payment.TransactionId;

    /// <summary>An incoming pacs.009: its <c>transactionId</c>, also the status reference of its own query.</summary>
    public static string? For(IncomingFiTransferRequestDto transfer) => transfer.TransactionId;

    /// <summary>An incoming pacs.004: the return's <c>id</c>.</summary>
    public static string? For(Pacs004PaymentReturnRequestDto payment) => payment.Id;

    /// <summary>An incoming pain.001: its <c>paymentInformationId</c>.</summary>
    public static string? For(Pain001PaymentInitiationDto initiation) => initiation.PaymentInformationId;

    /// <summary>An incoming camt.056: its <c>id</c> (Assgnmt/Id).</summary>
    public static string? For(Camt056RecallRequestDto recall) => recall.Id;

    /// <summary>An incoming camt.055: its <c>msgId</c> (Assgnmt/Id).</summary>
    public static string? For(Camt055CancellationRequestDto cancellation) => cancellation.MsgId;

    /// <summary>An incoming camt.029: its <c>id</c> (Assgnmt/Id).</summary>
    public static string? For(Camt029ResolutionOfInvestigationDto refusal) => refusal.Id;

    /// <summary>
    /// A status posted to <see cref="TransactionRestApiRoutes.ReceiveStatus"/>: <c>{clientReference}:{status}</c> for a
    /// transaction the core system sent, <c>in:{coreReference}:{status}</c> for the reversal of an incoming payment.
    /// </summary>
    public static string For(TransactionStatusDto status) => status.Direction == TransactionDirection.Incoming
        ? $"in:{status.CoreReference}:{status.Status}"
        : $"{status.ClientReference}:{status.Status}";
}
