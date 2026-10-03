using IPS.MiidleWear.Contracts.Pacs004;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;
using IPS.MiidleWear.Contracts.Pain001;
using IPS.MiidleWear.Contracts.Routing;
using IPS.MiidleWear.Contracts.Transactions;

namespace IPS.MiidleWear.Contracts.Abstractions;

/// <summary>
/// The contract the external client/core banking system implements: one interface for every inbound IPS payment the
/// gateway forwards. The gateway calls these operations as REST endpoints on the core side after it has received,
/// stored and (for non-pacs.008 types) acknowledged the message.
/// <para>
/// Each method's <see cref="RestEndpointAttribute"/> is the canonical endpoint the core system exposes; the gateway calls
/// exactly these routes by default (its CoreSystem:*ReceivePath settings can override them if a core system has to use
/// different paths).
/// </para>
/// <para>
/// Every answer must be final. For pacs.008 the result becomes the pacs.002 confirmation/rejection, which IPS needs
/// within the payment timeout.
/// </para>
/// </summary>
public interface IClientPaymentReceiver
{
    [RestEndpoint("POST", Pacs008RestApiRoutes.Receive, Tag = "Pacs008", MessageType = "pacs.008",
        Summary = "Receive an inbound pacs.008 (same shape as the send request, plus debtor.participantBic) and return the final posting result.")]
    Task<Pacs008PaymentResultDto> ReceivePacs008Async(Pacs008InstantPaymentRequestDto payment, CancellationToken cancellationToken);

    [RestEndpoint("POST", Pacs009RestApiRoutes.Receive, Tag = "Pacs009", MessageType = "pacs.009",
        Summary = "Receive an inbound pacs.009 FI-to-FI credit transfer.")]
    Task<Pacs008PaymentResultDto> ReceivePacs009Async(Pacs009PaymentRequestDto payment, CancellationToken cancellationToken);

    [RestEndpoint("POST", Pacs004RestApiRoutes.Receive, Tag = "Pacs004", MessageType = "pacs.004",
        Summary = "Receive an inbound pacs.004 payment return.")]
    Task<Pacs008PaymentResultDto> ReceivePacs004Async(Pacs004PaymentReturnRequestDto payment, CancellationToken cancellationToken);

    /// <summary>
    /// An incoming pain.001: a PISP asks this bank (the payer's bank) to make a payment (Annex D §3.2.11). IPS has
    /// already been acknowledged (MessageAck). The answer only says whether the core system took the request
    /// (ACCP) or could not (RJCT) — it is <b>not</b> the answer to IPS. That one the core system sends itself before
    /// the payment schema's Timeout Deadline (counted from <c>creationDateTime</c>): the payment as a pacs.008 with
    /// endToEndId = "PSP-" + paymentInformationId and acceptanceDateTime = creationDateTime, or a refusal as a pain.002.
    /// Without either, IPS rejects the initiation itself when the deadline passes.
    /// Idempotency-Key = paymentInformationId.
    /// </summary>
    [RestEndpoint("POST", Pain001RestApiRoutes.Receive, Tag = "PaymentInitiation", MessageType = "pain.001",
        Summary = "Receive an incoming pain.001 payment initiation from a PISP (answer to IPS: pacs.008 PSP- or pain.002).")]
    Task<Pacs008PaymentResultDto> ReceivePain001Async(Pain001PaymentInitiationDto initiation, CancellationToken cancellationToken);

    /// <summary>
    /// The gateway posts the status of a transaction the core system sent (final, or ManualReview) — the answer to
    /// the 202 "Processing" it got. Any 2xx counts as delivered. The same clientReference + status can arrive more
    /// than once (retries, several gateway nodes), so the core system must treat it idempotently
    /// (Idempotency-Key = clientReference:status). Until it is delivered the core system can also read it with
    /// GET <see cref="TransactionRestApiRoutes.Status"/>.
    /// <para>
    /// Also used for an incoming pacs.008 the core system accepted after IPS had already been answered RJCT (its
    /// answer was lost): <c>direction</c> = Incoming, <c>coreReference</c> = the payment, <c>status</c> = Rejected —
    /// the core system must reverse the posting (Idempotency-Key = in:coreReference:status).
    /// </para>
    /// </summary>
    [RestEndpoint("POST", TransactionRestApiRoutes.ReceiveStatus, Tag = "Transactions",
        Summary = "Receive the status of a transaction the core system sent.")]
    Task ReceiveTransactionStatusAsync(TransactionStatusDto status, CancellationToken cancellationToken);

    /// <summary>
    /// The gateway asks what the core system did with an incoming payment whose receive call got no answer (timeout,
    /// broken connection). Returns the same result the receive call would have returned: ACCP or RJCT when the core
    /// system processed it, <c>Status = "PDNG"</c> while it is still processing, and <c>404</c> when it never
    /// received the payment (the gateway then posts it again with the same Idempotency-Key — pacs.009/004 only).
    /// </summary>
    [RestEndpoint("GET", TransactionRestApiRoutes.PaymentStatus, Tag = "Transactions",
        Summary = "Return the outcome of an incoming payment (404 when it was never received).")]
    Task<Pacs008PaymentResultDto?> GetPaymentStatusAsync(InboundPaymentStatusQueryDto query, CancellationToken cancellationToken);
}
