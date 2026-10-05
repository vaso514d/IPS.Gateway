using IPS.Middleware.Application.Inbound.Composition;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using Microsoft.Extensions.Logging;

namespace IPS.Middleware.Infrastructure.Inbound.Workers;

public sealed class IncomingReplyWorker(
        InboundReplyChannel channel,
        InboundDispatchDiscovery discovery,
        IIncomingWorkflowExecution execution,
        IncomingWorkerOptions options,
        IncomingTransportSettings transport,
        InboundSchedulingOptions scheduling,
        TimeProvider time,
        ILogger<IncomingReplyWorker> logger)
    : IncomingWorker(options, time, logger)
{
    protected override Task RunAsync(CancellationToken stop, CancellationToken work) => Task.WhenAll(
        RefillAsync(token => discovery.RefillAsync(replies: true, token), scheduling.DiscoveryInterval, stop),
        DispatchAsync(channel, transport.Ips.ConnectionLimit - 1, execution.DeliverReplyAsync, stop, work));
}
