namespace IPS.Middleware.Infrastructure.Inbound;

public sealed class InboundProcessingChannel(InboundSchedulingOptions options) : InboundJournalChannel(options.Capacity);
