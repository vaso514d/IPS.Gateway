using IPS.Middleware.Application.Diagnostics;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Infrastructure.Diagnostics;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IPS.Middleware.Infrastructure.Inbound.Workers;

public sealed class IncomingReceiveWorker(
        IServiceScopeFactory scopes,
        InboundReceiptRegistration receipts,
        IncomingWorkerOptions options,
        IncomingTransportSettings transport,
        TimeProvider time,
        ILogger<IncomingReceiveWorker> logger)
    : IncomingWorker(options, time, logger)
{
    protected override async Task RunAsync(CancellationToken stop, CancellationToken work)
    {
        // One receive call may wait its whole timeout, then the loop may sleep for the longest of its delays.
        var period = transport.ReceiveTimeout + Options.EmptyDelay + Options.ErrorDelay + Options.MessageDelay;
        while (!stop.IsCancellationRequested)
        {
            Beat("IPS receive", period);
            try
            {
                IncomingReceiveResponse response;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    response = await scope.ServiceProvider.GetRequiredService<IIncomingReceiveClient>().ReceiveAsync(stop);
                }

                if (response.Transport.HttpStatusCode is < 200 or >= 300)
                {
                    throw new HttpRequestException($"IPS receive returned HTTP {response.Transport.HttpStatusCode}.");
                }

                if (response.Transport.Body.Length == 0)
                {
                    await Task.Delay(Options.EmptyDelay, Time, stop);
                    continue;
                }

                var receipt = CreateReceipt(response);
                using var receiving = WorkScope.Begin(Logger, "receive", receipt.Sequence, ("MessageType", receipt.MessageType));
                await PersistReceiptAsync(receipt, work);
                await AcknowledgeAsync(receipt, stop);
                await Task.Delay(Options.MessageDelay, Time, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested || work.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                Logger.LogError(error, "IPS receive failed; polling will retry");
                PaymentMetrics.ErrorLogged(nameof(IncomingReceiveWorker));
                await Task.Delay(Options.ErrorDelay, Time, stop);
            }
        }
    }

    private static InboundReceipt CreateReceipt(IncomingReceiveResponse response)
    {
        // Unsupported transport metadata is held by processing; preserve XML without parsing here.
        var messageType = response.MessageType?.Trim();
        if (string.IsNullOrEmpty(messageType) || messageType.Length > 35)
        {
            messageType = "unsupported";
        }

        return new InboundReceipt(response.ParticipantBic, response.Sequence, messageType,
            response.Transport.Body, response.PossibleDuplicate, response.ReceivedAtUtc);
    }

    // IPS expects a status report, a financial-institution transfer, a payment return, a payment initiation, a recall or a
    // cancellation to be acknowledged, and only once it is stored. A failed acknowledgement never stops polling: IPS
    // redelivers the message and the duplicate receipt is acknowledged again.
    private async Task AcknowledgeAsync(InboundReceipt receipt, CancellationToken token)
    {
        var needsAcknowledgement = PaymentMessageTypes.IsPacs002(receipt.MessageType)
            || PaymentMessageTypes.IsIncomingTransfer(receipt.MessageType)
            || PaymentMessageTypes.IsArchivedCancellation(receipt.MessageType);
        if (!needsAcknowledgement || receipt.Sequence is not > 0)
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var client = scope.ServiceProvider.GetRequiredService<IIncomingAckClient>();
            var response = await client.AcknowledgeAsync(receipt.ParticipantBic, receipt.Sequence.Value, token);
            var acknowledged = response.HttpStatusCode is >= 200 and < 300;
            PaymentMetrics.Acknowledged(acknowledged ? "ok" : "http_error");
            if (!acknowledged)
            {
                PaymentMetrics.ErrorLogged("IncomingAcknowledgement");
                Logger.LogWarning("IPS acknowledgement of sequence {Sequence} returned HTTP {Status}", receipt.Sequence, response.HttpStatusCode);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
        {
            PaymentMetrics.Acknowledged("failed");
            PaymentMetrics.ErrorLogged("IncomingAcknowledgement");
            Logger.LogWarning(error, "IPS acknowledgement of sequence {Sequence} failed; redelivery will repeat it", receipt.Sequence);
        }
    }

    private async Task PersistReceiptAsync(InboundReceipt receipt, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                var registered = await receipts.RegisterAsync(receipt, cancellationToken);
                PaymentMetrics.ReceiptReceived(receipt.MessageType, redelivery: !registered.Created);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                Logger.LogError(error, "Receipt commit failed; retrying before the next IPS receive");
                PaymentMetrics.ErrorLogged(nameof(IncomingReceiveWorker));
                // A SQL outage is a stall of its own that readiness should report, but the receive loop is still alive and retrying.
                Beat("IPS receive", Options.ErrorDelay + Options.ErrorDelay);
                await Task.Delay(Options.ErrorDelay, Time, cancellationToken);
            }
        }
    }

}
