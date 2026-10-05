using System.Threading.Channels;

namespace IPS.Middleware.Infrastructure.Inbound;

/// <summary>Process-local bounded FIFO of journal IDs that coalesces queued IDs; SQL stays authoritative.</summary>
public abstract class InboundJournalChannel(int capacity)
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(capacity);
    private readonly HashSet<Guid> _queued = [];
    private readonly Lock _gate = new();

    public bool TryNotify(Guid journalId)
    {
        lock (_gate)
        {
            if (_queued.Contains(journalId) || !_channel.Writer.TryWrite(journalId))
            {
                return false;
            }

            _queued.Add(journalId);
            return true;
        }
    }

    public bool TryRead(out Guid journalId)
    {
        lock (_gate)
        {
            if (!_channel.Reader.TryRead(out journalId))
            {
                return false;
            }

            _queued.Remove(journalId);
            return true;
        }
    }

    public async ValueTask<Guid> ReadAsync(CancellationToken cancellationToken)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            if (TryRead(out var id))
            {
                return id;
            }
        }

        throw new ChannelClosedException();
    }
}
