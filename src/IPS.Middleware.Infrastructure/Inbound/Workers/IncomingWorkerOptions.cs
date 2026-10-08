using IPS.Middleware.Infrastructure.Inbound.Transport;

namespace IPS.Middleware.Infrastructure.Inbound.Workers;

public sealed class IncomingWorkerOptions
{
    public bool Enabled { get; init; }
    public int CbsFollowUpCapacity { get; init; } = 2;
    public int AcknowledgementCapacity { get; init; } = 2;
    public int AcknowledgementBacklog { get; init; } = 1000;
    public TimeSpan MessageDelay { get; init; } = TimeSpan.Zero;
    public TimeSpan EmptyDelay { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan ErrorDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan ShutdownBudget { get; init; } = TimeSpan.FromSeconds(30);

    public void Validate(IncomingTransportSettings transport)
    {
        if (CbsFollowUpCapacity <= 0 || AcknowledgementCapacity <= 0 || AcknowledgementBacklog <= 0 || MessageDelay < TimeSpan.Zero ||
            EmptyDelay <= TimeSpan.Zero || ErrorDelay <= TimeSpan.Zero || ShutdownBudget <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Incoming worker capacities and delays must be positive (message delay may be zero).");
        }

        if (!Enabled)
        {
            return;
        }

        if (!transport.Enabled)
        {
            throw new InvalidOperationException("Incoming workers require enabled transport.");
        }

        if (IpsSendCapacity(transport) <= 0 || transport.Cbs.ConnectionLimit <= CbsFollowUpCapacity)
        {
            throw new InvalidOperationException(
                "Incoming connections must leave capacity after the receive, acknowledgement and CBS follow-up reservations.");
        }
    }

    // IPS connections left for replies once receive and the acknowledgements have theirs.
    public int IpsSendCapacity(IncomingTransportSettings transport) => transport.Ips.ConnectionLimit - 1 - AcknowledgementCapacity;

    public int ProcessingCapacity(IncomingTransportSettings transport) =>
        Math.Min(IpsSendCapacity(transport), transport.Cbs.ConnectionLimit - CbsFollowUpCapacity);
}
