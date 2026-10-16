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

namespace IPS.MiidleWear.CoreReceiver.Sample;

// The bank's part: the nine operations. IPS.MiidleWear.AspNetCore maps them to the gateway's routes, JSON and status codes.
public sealed class SampleReceiver(CoreLedger ledger, IIpsReceiveContext context, IOptions<SampleOptions> options, TimeProvider timeProvider)
    : IClientPaymentReceiver
{
    // A pacs.008 delivery carries no message or transaction id, so only the end-to-end id is echoed.
    public Task<Pacs008PaymentResultDto> ReceivePacs008Async(Pacs008InstantPaymentRequestDto payment, CancellationToken cancellationToken) =>
        ReceiveAsync("pacs.008", IpsMessageKind.Pacs008, IpsIdempotencyKeys.For(payment), new Echo(null, payment.EndToEndId, null));

    public Task<Pacs008PaymentResultDto> ReceivePacs009Async(Pacs009PaymentRequestDto payment, CancellationToken cancellationToken) =>
        ReceiveAsync("pacs.009", IpsMessageKind.Pacs009, IpsIdempotencyKeys.For(payment), new Echo(payment.Id, payment.EndToEndId, payment.TxId));

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

    private Pacs008PaymentResultDto Answer(string kind, string key, Echo echo)
    {
        var configured = options.Value.Answers.GetValueOrDefault(kind, "Accept");
        return configured.StartsWith("Reject:", StringComparison.OrdinalIgnoreCase)
            ? Pacs008PaymentResultDto.Reject(echo.Id, echo.EndToEndId, echo.TxId, configured["Reject:".Length..], description: "Refused by the sample core.")
            : Pacs008PaymentResultDto.Accept(echo.Id, echo.EndToEndId, echo.TxId, coreReference: "CORE-" + key);
    }
}
