using IPS.Middleware.Application.Inbound.Composition;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using Microsoft.Extensions.Logging;

namespace IPS.Middleware.Infrastructure.Inbound.Workers;

public sealed class IncomingProcessingWorker(
        InboundProcessingChannel channel,
        InboundDispatchDiscovery discovery,
        IncomingComposition composition,
        IncomingWorkerOptions options,
        IncomingTransportSettings transport,
        InboundSchedulingOptions scheduling,
        TimeProvider time,
        ILogger<IncomingProcessingWorker> logger)
    : IncomingWorker(options, time, logger)
{
    protected override Task RunAsync(CancellationToken stop, CancellationToken work) => Task.WhenAll(
        RefillAsync(token => discovery.RefillAsync(replies: false, token), scheduling.DiscoveryInterval, stop),
        DispatchAsync(channel, Options.ProcessingCapacity(transport),
            composition.ProcessAsync, stop, work));
}
