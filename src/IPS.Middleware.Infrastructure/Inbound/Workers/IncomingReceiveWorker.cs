using System.Threading.Channels;
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
    private const string ReceiveLoop = "IPS receive";
    private const string AcknowledgementLoop = "IPS acknowledgement";

    // Polling never waits for a MessageAck: committed receipts are queued and acknowledged by a loop of their own, which
    // drains the queue after polling stops, within the shutdown budget.
    protected override async Task RunAsync(CancellationToken stop, CancellationToken work)
    {
        var acknowledgements = Channel.CreateBounded<Acknowledgement>(new BoundedChannelOptions(Options.AcknowledgementBacklog)
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        var acknowledging = AcknowledgeQueuedAsync(acknowledgements.Reader, work);
        try
        {
            await ReceiveAsync(acknowledgements.Writer, stop, work);
        }
        finally
        {
            acknowledgements.Writer.Complete();
            await acknowledging;
        }
    }

    private async Task ReceiveAsync(ChannelWriter<Acknowledgement> acknowledgements, CancellationToken stop, CancellationToken work)
    {
        // One receive call may wait its whole timeout, then the loop may sleep for the longest of its delays.
        var period = transport.ReceiveTimeout + Options.EmptyDelay + Options.ErrorDelay + Options.MessageDelay;
        while (!stop.IsCancellationRequested)
        {
            Beat(ReceiveLoop, period);
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
                QueueAcknowledgement(acknowledgements, receipt);
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
    // cancellation to be acknowledged, and only once it is stored. A failed or skipped acknowledgement loses nothing: IPS
    // redelivers the message and the duplicate receipt is acknowledged again.
    private void QueueAcknowledgement(ChannelWriter<Acknowledgement> acknowledgements, InboundReceipt receipt)
    {
        if (!NeedsAcknowledgement(receipt, out var sequence))
        {
            return;
        }

        var queued = acknowledgements.TryWrite(new(receipt.ParticipantBic, sequence, receipt.MessageType));
        if (queued)
        {
            return;
        }

        PaymentMetrics.Acknowledged("skipped");
        PaymentMetrics.ErrorLogged("IncomingAcknowledgement");
        Logger.LogWarning("The acknowledgement queue is full; sequence {Sequence} is left to redelivery", receipt.Sequence);
    }

    private static bool NeedsAcknowledgement(InboundReceipt receipt, out long sequence)
    {
        sequence = receipt.Sequence ?? 0;
        var acknowledgedType = PaymentMessageTypes.IsPacs002(receipt.MessageType)
            || PaymentMessageTypes.IsIncomingTransfer(receipt.MessageType)
            || PaymentMessageTypes.IsArchivedCancellation(receipt.MessageType);
        return acknowledgedType && sequence > 0;
    }

    // At most AcknowledgementCapacity acknowledgements run at once. The loop promises a beat within one IPS request timeout, the
    // longest one acknowledgement may take. Its beat is as old as the oldest acknowledgement still running, so one that hangs
    // shows as a stall of this loop alone, even while the other slots keep working. The loop wakes whenever an
    // acknowledgement finishes, so its beat moves on as soon as the oldest one completes.
    private async Task AcknowledgeQueuedAsync(ChannelReader<Acknowledgement> queue, CancellationToken work)
    {
        var period = transport.Ips.RequestTimeout;
        var running = new Dictionary<Task, DateTimeOffset>();
        try
        {
            while (true)
            {
                var finished = running.Keys
                    .Where(task => task.IsCompleted)
                    .ToArray();
                foreach (var task in finished)
                {
                    running.Remove(task);
                }

                Beat(AcknowledgementLoop, period, OldestStart(running));
                if (running.Count == Options.AcknowledgementCapacity)
                {
                    await Task.WhenAny(running.Keys);
                    continue;
                }

                using var idle = new CancellationTokenSource(period, Time);
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(idle.Token, work);
                try
                {
                    if (!await WaitForQueueOrCompletionAsync(queue, running.Keys, wait))
                    {
                        return;
                    }
                }
                catch (OperationCanceledException) when (!work.IsCancellationRequested)
                {
                    continue;
                }

                if (queue.TryRead(out var acknowledgement))
                {
                    running.Add(AcknowledgeAsync(acknowledgement, work), Time.GetUtcNow());
                }
            }
        }
        finally
        {
            await Task.WhenAll(running.Keys);
        }
    }

    private DateTimeOffset OldestStart(Dictionary<Task, DateTimeOffset> running) =>
        running.Count == 0 ? Time.GetUtcNow() : running.Values.Min();

    // Waits for a queued acknowledgement, or for a running one to finish so the loop beats again. False once the queue is
    // completed and empty; the wait's cancellation (idle period or work) surfaces as OperationCanceledException.
    private static async Task<bool> WaitForQueueOrCompletionAsync(
        ChannelReader<Acknowledgement> queue,
        IEnumerable<Task> running,
        CancellationTokenSource wait)
    {
        var queued = queue.WaitToReadAsync(wait.Token).AsTask();
        var first = await Task.WhenAny(running.Append(queued));
        if (first == queued)
        {
            return await queued;
        }

        // Stop waiting on the queue; the next pass reads anything that arrived meanwhile.
        await wait.CancelAsync();
        try
        {
            await queued;
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        {
        }

        return true;
    }

    private async Task AcknowledgeAsync(Acknowledgement acknowledgement, CancellationToken token)
    {
        using var acknowledging = WorkScope.Begin(Logger, "acknowledge", acknowledgement.Sequence, ("MessageType", acknowledgement.MessageType));
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var client = scope.ServiceProvider.GetRequiredService<IIncomingAckClient>();
            var response = await client.AcknowledgeAsync(acknowledgement.ParticipantBic, acknowledgement.Sequence, token);
            var acknowledged = response.HttpStatusCode is >= 200 and < 300;
            PaymentMetrics.Acknowledged(acknowledged ? "ok" : "http_error");
            if (!acknowledged)
            {
                PaymentMetrics.ErrorLogged("IncomingAcknowledgement");
                Logger.LogWarning("IPS acknowledgement of sequence {Sequence} returned HTTP {Status}", acknowledgement.Sequence, response.HttpStatusCode);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
        {
            PaymentMetrics.Acknowledged("failed");
            PaymentMetrics.ErrorLogged("IncomingAcknowledgement");
            Logger.LogWarning(error, "IPS acknowledgement of sequence {Sequence} failed; redelivery will repeat it", acknowledgement.Sequence);
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
                Beat(ReceiveLoop, Options.ErrorDelay + Options.ErrorDelay);
                await Task.Delay(Options.ErrorDelay, Time, cancellationToken);
            }
        }
    }

    private sealed record Acknowledgement(string ParticipantBic, long Sequence, string MessageType);
}
