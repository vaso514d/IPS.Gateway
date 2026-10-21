using IPS.MiidleWear.AspNetCore;
using IPS.MiidleWear.Contracts.Abstractions;
using IPS.MiidleWear.Contracts.Camt029;
using IPS.MiidleWear.Contracts.Camt055;
using IPS.MiidleWear.Contracts.Camt056;
using IPS.MiidleWear.Contracts.Http;
using IPS.MiidleWear.Contracts.Pacs004;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;
using IPS.MiidleWear.Contracts.Pain001;
using IPS.MiidleWear.Contracts.Transactions;
using Microsoft.Extensions.Options;

namespace IPS.MiidleWear.CoreAPI;

// The bank's part: the eleven operations. IPS.MiidleWear.AspNetCore maps them to the gateway's routes, JSON and status codes.
public sealed class SampleReceiver(CoreLedger ledger, IIpsReceiveContext context, IOptions<SampleOptions> options, TimeProvider timeProvider)
    : IClientPaymentReceiver
{
    // An incoming instant payment is posted once per transactionId, like every other kind, and answered in its own envelope.
    public async Task<IncomingPaymentResultDto> ReceiveIncomingPaymentAsync(IncomingPaymentRequestDto payment, CancellationToken cancellationToken)
    {
        var transactionId = context.IdempotencyKey ?? IpsIdempotencyKeys.For(payment)
            ?? throw new InvalidOperationException("The delivery carries no transactionId.");
        var answer = await ReceiveAsync("pacs.008", IpsMessageKind.Pacs008, transactionId, new Echo(null, null, null));
        return ToIncomingPayment(transactionId, answer);
    }

    public Task<IncomingPaymentResultDto?> GetIncomingPaymentAsync(string transactionId, CancellationToken cancellationToken) =>
        Task.FromResult(ledger.Find(IpsMessageKind.Pacs008, transactionId) is { } answer ? ToIncomingPayment(transactionId, answer) : null);

    // An incoming FI transfer is posted once per transactionId. The sample posts within the call, so it answers COMPLETED
    // (or REJECTED); a core system that processes later answers ACCEPTED_FOR_PROCESSING and reports the result on the query.
    public async Task<IncomingFiTransferResultDto> ReceiveFiTransferAsync(IncomingFiTransferRequestDto transfer, CancellationToken cancellationToken)
    {
        var transactionId = context.IdempotencyKey ?? IpsIdempotencyKeys.For(transfer)
            ?? throw new InvalidOperationException("The delivery carries no transactionId.");
        var answer = await ReceiveAsync("pacs.009", IpsMessageKind.Pacs009, transactionId, new Echo(null, null, null));
        return ToFiTransfer(transactionId, answer);
    }

    public Task<IncomingFiTransferResultDto?> GetFiTransferAsync(string transactionId, CancellationToken cancellationToken) =>
        Task.FromResult(ledger.Find(IpsMessageKind.Pacs009, transactionId) is { } answer ? ToFiTransfer(transactionId, answer) : null);

    public Task<Pacs008PaymentResultDto> ReceivePacs004Async(Pacs004PaymentReturnRequestDto payment, CancellationToken cancellationToken) =>
        ReceiveAsync("pacs.004", IpsMessageKind.Pacs004, IpsIdempotencyKeys.For(payment), new Echo(payment.Id, null, null));

    // Taking an initiation only acknowledges it; the core answers IPS later with a PSP- pacs.008 or a pain.002.
    public Task<Pacs008PaymentResultDto> ReceivePain001Async(Pain001PaymentInitiationDto initiation, CancellationToken cancellationToken) =>
        ReceiveAsync("pain.001", IpsMessageKind.Pain001, IpsIdempotencyKeys.For(initiation), new Echo(initiation.PaymentInformationId, null, null));

    // Taking a recall only acknowledges it; the core answers it later with a pacs.004 (return) or a camt.029 (refusal).
    public Task<Pacs008PaymentResultDto> ReceiveCamt056Async(Camt056RecallRequestDto recall, CancellationToken cancellationToken) =>
        ReceiveAsync("camt.056", IpsMessageKind.Camt056, IpsIdempotencyKeys.For(recall), new Echo(recall.Id, null, null));

    public Task<Pacs008PaymentResultDto> ReceiveCamt055Async(Camt055CancellationRequestDto cancellation, CancellationToken cancellationToken) =>
        ReceiveAsync("camt.055", IpsMessageKind.Camt055, IpsIdempotencyKeys.For(cancellation), new Echo(cancellation.MsgId, null, null));

    public Task<Pacs008PaymentResultDto> ReceiveCamt029Async(Camt029ResolutionOfInvestigationDto refusal, CancellationToken cancellationToken) =>
        ReceiveAsync("camt.029", IpsMessageKind.Camt029, IpsIdempotencyKeys.For(refusal), new Echo(refusal.Id, null, null));

    public Task ReceiveTransactionStatusAsync(TransactionStatusDto status, CancellationToken cancellationToken)
    {
        ledger.RecordStatus(context.IdempotencyKey ?? IpsIdempotencyKeys.For(status), status);
        if (status is { Direction: TransactionDirection.Incoming, Status: TransactionStatus.Rejected, CoreReference: { } coreReference })
        {
            ledger.Reverse(coreReference);
        }

        return Task.CompletedTask;
    }

    public Task<Pacs008PaymentResultDto?> GetPaymentStatusAsync(InboundPaymentStatusQueryDto query, CancellationToken cancellationToken) =>
        Task.FromResult(query is { MessageKind: { } kind, Reference: { } reference } ? ledger.Find(kind, reference) : null);

    private Task<Pacs008PaymentResultDto> ReceiveAsync(string kind, IpsMessageKind messageKind, string? derivedKey, Echo echo)
    {
        var key = context.IdempotencyKey ?? derivedKey
            ?? throw new InvalidOperationException("The delivery carries no Idempotency-Key.");
        return ledger.PostAsync(messageKind, key, echo, async token =>
        {
            await Task.Delay(options.Value.PostingDelay, timeProvider, token);
            return Answer(kind, key, echo);
        });
    }

    private static IncomingPaymentResultDto ToIncomingPayment(string transactionId, Pacs008PaymentResultDto answer) => answer.Status switch
    {
        Pacs008PaymentStatuses.Accepted => IncomingPaymentResultDto.Accepted(transactionId, answer.CoreReference),
        Pacs008PaymentStatuses.Rejected => IncomingPaymentResultDto.Rejected(transactionId, answer.ReasonCode!, answer.Description),
        _ => IncomingPaymentResultDto.Pending(transactionId)
    };

    private static IncomingFiTransferResultDto ToFiTransfer(string transactionId, Pacs008PaymentResultDto answer) => answer.Status switch
    {
        Pacs008PaymentStatuses.Accepted => IncomingFiTransferResultDto.Completed(transactionId, answer.CoreReference),
        Pacs008PaymentStatuses.Rejected => IncomingFiTransferResultDto.Rejected(transactionId, answer.ReasonCode!, answer.Description),
        _ => IncomingFiTransferResultDto.Processing(transactionId)
    };

    private Pacs008PaymentResultDto Answer(string kind, string key, Echo echo)
    {
        var configured = options.Value.Answers.GetValueOrDefault(kind, "Accept");
        return configured.StartsWith("Reject:", StringComparison.OrdinalIgnoreCase)
            ? Pacs008PaymentResultDto.Reject(echo.Id, echo.EndToEndId, echo.TxId, configured["Reject:".Length..], description: "Refused by the sample core.")
            : Pacs008PaymentResultDto.Accept(echo.Id, echo.EndToEndId, echo.TxId, coreReference: "CORE-" + key);
    }
}
