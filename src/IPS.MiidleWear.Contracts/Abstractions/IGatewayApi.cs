using IPS.MiidleWear.Contracts.Camt029;
using IPS.MiidleWear.Contracts.Camt056;
using IPS.MiidleWear.Contracts.Pacs004;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;
using IPS.MiidleWear.Contracts.Pain002;
using IPS.MiidleWear.Contracts.Proxy;
using IPS.MiidleWear.Contracts.Routing;
using IPS.MiidleWear.Contracts.Transactions;

namespace IPS.MiidleWear.Contracts.Abstractions;

/// <summary>
/// Everything the core banking system can call on the gateway — one interface for the single participant API
/// (IPS payments, the recall flow and Proxy Solution management). Its counterpart on the core side is
/// <see cref="IClientPaymentReceiver"/>.
/// <para>
/// Each method's <see cref="RestEndpointAttribute"/> is its canonical REST endpoint; the gateway builds its endpoints
/// from these attributes, so a route exists only here. Sends are asynchronous: 202 + status Processing once the
/// transaction is stored; the final status is posted to the core system and readable with GET. 400 is validation.
/// </para>
/// </summary>
public interface IGatewayApi
{
    [RestEndpoint("POST", Pacs008RestApiRoutes.Send, Tag = "Pacs008", MessageType = "pacs.008", SuccessStatusCode = 202,
        Summary = "Send an outbound pacs.008 instant payment.",
        Description = "Accepts a Bank/Core instant-payment request and answers 202 with status Processing; the gateway sends it to IPS, recovers an unknown outcome (pacs.008 status investigation) and delivers the final status to the core system. It generates MsgId, TxId, the settlement date and every fixed XML value. clientReference is the idempotency key across every message type: a repeated request returns the existing transaction. 400 = validation.")]
    Task<TransactionStatusDto> SendPacs008Async(Pacs008InstantPaymentRequestDto payment, CancellationToken cancellationToken);

    [RestEndpoint("POST", Pacs009RestApiRoutes.Send, Tag = "Pacs009", MessageType = "pacs.009", SuccessStatusCode = 202,
        Summary = "Send an outbound pacs.009 FI-to-FI credit transfer.",
        Description = "Accepts the request and answers 202 with status Processing; the final status is delivered to the core system (and readable with GET /api/ips/transactions/status). clientReference is the idempotency key. 400 = validation.")]
    Task<TransactionStatusDto> SendPacs009Async(Pacs009PaymentRequestDto payment, CancellationToken cancellationToken);

    [RestEndpoint("POST", Pacs004RestApiRoutes.Send, Tag = "Pacs004", MessageType = "pacs.004", SuccessStatusCode = 202,
        Summary = "Send an outbound pacs.004 payment return.",
        Description = "A payment return — also the positive answer to a received camt.056 recall. Answers 202 with status Processing; the final status is delivered to the core system. clientReference is the idempotency key.")]
    Task<TransactionStatusDto> SendPacs004Async(Pacs004PaymentReturnRequestDto payment, CancellationToken cancellationToken);

    [RestEndpoint("POST", Camt056RestApiRoutes.Send, Tag = "Recall", MessageType = "camt.056", SuccessStatusCode = 202,
        Summary = "Send a camt.056 request for recall of a pacs.008 this bank sent.",
        Description = "Asks IPS to recall a completed outbound instant credit transfer (NBG profile IPS_camt.056 - V1). Answers 202; the final status is IPS's technical verdict, delivered to the core system. The creditor bank answers later with an inbound pacs.004 (funds returned) or camt.029 (refused).")]
    Task<TransactionStatusDto> SendCamt056Async(Camt056RecallRequestDto request, CancellationToken cancellationToken);

    [RestEndpoint("POST", Camt029RestApiRoutes.Send, Tag = "Recall", MessageType = "camt.029", SuccessStatusCode = 202,
        Summary = "Send a camt.029 negative answer to a received camt.056 recall.",
        Description = "Refuses a recall this bank received as the creditor (NBG profile IPS_camt.029 - V1; Conf and TxCxlSts are always RJCR). Answers 202; the final status is delivered to the core system. To accept a recall, send a pacs.004 instead.")]
    Task<TransactionStatusDto> SendCamt029Async(Camt029ResolutionOfInvestigationDto request, CancellationToken cancellationToken);

    [RestEndpoint("POST", Pain002RestApiRoutes.Send, Tag = "PaymentInitiation", MessageType = "pain.002", SuccessStatusCode = 202,
        Summary = "Send a pain.002 refusing a received pain.001 payment initiation.",
        Description = "This bank, as the originator participant, refuses a payment a PISP initiated (Annex D §3.2.11–§3.2.12; GrpSts and PmtInfSts are always RJCT). Answers 202; IPS replies with a pain.002 and forwards the refusal to the PISP, and the final status (Accepted = IPS took the refusal) is delivered to the core system. To accept the initiation, send the payment as a pacs.008 whose endToEndId is \"PSP-\" + the pain.001 PmtInfId.")]
    Task<TransactionStatusDto> SendPain002Async(Pain002PaymentStatusReportDto request, CancellationToken cancellationToken);

    [RestEndpoint("GET", TransactionRestApiRoutes.Status, Tag = "Transactions",
        Summary = "Read the current status of a transaction the core system sent.",
        Description = "By messageKind and clientReference. A transaction whose status is final is thereby marked as delivered to the core system (the gateway stops re-posting it). 404 when no such transaction exists.")]
    Task<TransactionStatusDto> GetTransactionStatusAsync(TransactionStatusQueryDto query, CancellationToken cancellationToken);

    [RestEndpoint("POST", ProxyRestApiRoutes.Register, Tag = "Proxy", MessageType = "acmt.022", Upstream = "Proxy Solution",
        Summary = "Register an account holder, account and proxy identifiers with the Proxy Solution.",
        Description = "Annex E §1.2.1 Registration. Accept and reject both return HTTP 200 with ProxyOperationResultDto.Accepted.")]
    Task<ProxyOperationResultDto> RegisterProxyAsync(ProxyRegisterRequestDto request, CancellationToken cancellationToken);

    [RestEndpoint("POST", ProxyRestApiRoutes.Update, Tag = "Proxy", MessageType = "acmt.022", Upstream = "Proxy Solution",
        Summary = "Update account holder, account or proxy identifier information already registered with the Proxy Solution.",
        Description = "Annex E §1.2.2 Update. The account holder identifier and type identify the existing record; every other field is optional.")]
    Task<ProxyOperationResultDto> UpdateProxyAsync(ProxyUpdateRequestDto request, CancellationToken cancellationToken);

    [RestEndpoint("POST", ProxyRestApiRoutes.Remove, Tag = "Proxy", MessageType = "acmt.022", Upstream = "Proxy Solution",
        Summary = "Remove an account, proxy identifiers, authorized persons or beneficial owners from the Proxy Solution.",
        Description = "Annex E §1.2.2 Removal: the whole account (cascading), or with KeepAccountActive only the listed items.")]
    Task<ProxyOperationResultDto> RemoveProxyAsync(ProxyRemoveRequestDto request, CancellationToken cancellationToken);
}
