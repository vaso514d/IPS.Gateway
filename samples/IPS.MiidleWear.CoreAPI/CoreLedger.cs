using System.Collections.Concurrent;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Transactions;

namespace IPS.MiidleWear.CoreAPI;

// The identifiers an answer echoes back (see each receive operation's description).
public sealed record Echo(string? Id, string? EndToEndId, string? TxId);

// An in-memory stand-in for the core banking system's own storage. A real core keeps the same three facts durably: one
// posting per Idempotency-Key, its answer, and every status it was told.
public sealed class CoreLedger(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<(IpsMessageKind Kind, string Key), Posting> _postings = new();
    private readonly ConcurrentDictionary<string, TransactionStatusDto> _statuses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _reversals = new(StringComparer.Ordinal);

    // One posting per key: a redelivery joins the posting already started and gets its answer, never a second credit.
    // A failed posting is forgotten, so the gateway's next delivery tries again.
    public async Task<Pacs008PaymentResultDto> PostAsync(
        IpsMessageKind kind,
        string key,
        Echo echo,
        Func<CancellationToken, Task<Pacs008PaymentResultDto>> post)
    {
        var posting = _postings.GetOrAdd((kind, key), _ => new Posting(echo, timeProvider.GetUtcNow(),
            new Lazy<Task<Pacs008PaymentResultDto>>(() => post(CancellationToken.None))));
        try
        {
            return await posting.Answer.Value;
        }
        catch
        {
            _postings.TryRemove(new KeyValuePair<(IpsMessageKind, string), Posting>((kind, key), posting));
            throw;
        }
    }

    // The payment-status query: the answer once posted, PDNG while posting, null (404) for a message never received.
    public Pacs008PaymentResultDto? Find(IpsMessageKind kind, string reference)
    {
        if (!_postings.TryGetValue((kind, reference), out var posting))
        {
            return null;
        }

        var answer = posting.Answer.Value;
        return answer.IsCompletedSuccessfully
            ? answer.Result
            : Pacs008PaymentResultDto.Pending(posting.Echo.Id, posting.Echo.EndToEndId, posting.Echo.TxId, "Posting in progress.");
    }

    // Statuses arrive again on retries; the first one for a key is kept.
    public void RecordStatus(string key, TransactionStatusDto status) => _statuses.TryAdd(key, status);

    // The gateway asks to reverse an incoming pacs.008 credited after IPS was told RJCT; a real core reverses the posting.
    public void Reverse(string coreReference) => _reversals.TryAdd(coreReference, timeProvider.GetUtcNow());

    public object Snapshot() => new
    {
        postings = _postings.Select(entry => new
        {
            kind = entry.Key.Kind.ToString(),
            key = entry.Key.Key,
            receivedAtUtc = entry.Value.ReceivedAtUtc,
            status = entry.Value.Answer.Value.IsCompletedSuccessfully ? entry.Value.Answer.Value.Result.Status : Pacs008PaymentStatuses.Pending
        }),
        statuses = _statuses.Select(entry => new { key = entry.Key, entry.Value.ClientReference, entry.Value.CoreReference, status = entry.Value.Status.ToString() }),
        reversals = _reversals.Keys
    };

    private sealed record Posting(Echo Echo, DateTimeOffset ReceivedAtUtc, Lazy<Task<Pacs008PaymentResultDto>> Answer);
}
