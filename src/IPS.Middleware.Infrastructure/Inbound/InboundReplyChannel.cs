namespace IPS.Middleware.Infrastructure.Inbound;

public sealed class InboundReplyChannel(InboundSchedulingOptions options) : InboundJournalChannel(options.Capacity);
