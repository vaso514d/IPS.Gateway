using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using IPS.MiidleWear.Contracts.Http;
using IPS.MiidleWear.Contracts.Transactions;

namespace IPS.MiidleWear.Testing;

/// <summary>What the checks put into the messages they send.</summary>
public sealed record IpsReceiverConformanceOptions
{
    /// <summary>The bank under test, as creditor agent of incoming payments and debtor agent of initiations and recalls.</summary>
    public string ParticipantBic { get; init; } = "TESTGE22";

    /// <summary>The account incoming payments credit; use a test account of the core system.</summary>
    public string CreditAccount { get; init; } = "GE29NB0000000101904917";

    /// <summary>The currency of the sample payments.</summary>
    public string Currency { get; init; } = "GEL";
}

/// <summary>One conformance check and its outcome.</summary>
/// <param name="Name">What was checked.</param>
/// <param name="Passed">Whether the receiver behaved as the gateway requires.</param>
/// <param name="Reason">What was observed.</param>
public sealed record IpsReceiverCheck(string Name, bool Passed, string Reason);

/// <summary>The outcome of <see cref="IpsReceiverConformance.RunAsync"/>.</summary>
public sealed class IpsReceiverConformanceResult(IReadOnlyList<IpsReceiverCheck> checks)
{
    /// <summary>Every check, in the order it ran.</summary>
    public IReadOnlyList<IpsReceiverCheck> Checks { get; } = checks;

    /// <summary>The checks that failed.</summary>
    public IReadOnlyList<IpsReceiverCheck> Failures => Checks.Where(check => !check.Passed).ToArray();

    /// <summary>True when every check passed.</summary>
    public bool Passed => Failures.Count == 0;

    /// <summary>Throws <see cref="IpsReceiverConformanceException"/> listing the failures, so a test of any framework fails.</summary>
    public void EnsurePassed()
    {
        if (!Passed)
        {
            throw new IpsReceiverConformanceException(this);
        }
    }

    /// <summary>One line per check: PASS or FAIL, the check and the reason.</summary>
    public override string ToString() =>
        string.Join(Environment.NewLine, Checks.Select(check => $"{(check.Passed ? "PASS" : "FAIL")}  {check.Name}: {check.Reason}"));
}

/// <summary>Thrown by <see cref="IpsReceiverConformanceResult.EnsurePassed"/> when a check failed.</summary>
public sealed class IpsReceiverConformanceException(IpsReceiverConformanceResult result)
    : Exception($"The receiver failed {result.Failures.Count} of {result.Checks.Count} conformance checks:{Environment.NewLine}{result}")
{
    /// <summary>The full result.</summary>
    public IpsReceiverConformanceResult Result { get; } = result;
}

/// <summary>
/// Sends a core system's receiver what the IPS middleware sends (the gateway's JSON with <c>null</c> members, and the
/// Idempotency-Key) and reads each answer with the gateway's rules. For each of the seven receive kinds: the answer is
/// final, a redelivery gets the same answer, and the payment-status query returns it. Then: an unknown reference answers
/// 404, and the status callback and a reversal are accepted, also when redelivered. Every message carries identifiers
/// unique to the run, so a run can be repeated against the same receiver.
/// </summary>
public static class IpsReceiverConformance
{
    // What the gateway's JsonContent sends: web defaults, null members included.
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Runs every check against the receiver at <paramref name="client"/>'s base address (the URL the gateway is
    /// configured with as the CBS base URL).
    /// </summary>
    public static async Task<IpsReceiverConformanceResult> RunAsync(
        HttpClient client,
        IpsReceiverConformanceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var run = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var checks = new List<IpsReceiverCheck>();
        foreach (var sample in SampleMessages.All(run, options ?? new()))
        {
            await CheckKindAsync(client, sample, checks, cancellationToken);
        }

        await CheckUnknownReferenceAsync(client, run, checks, cancellationToken);
        await CheckCallbackAsync(client, "status callback", SampleMessages.OutgoingStatus(run), checks, cancellationToken);
        await CheckCallbackAsync(client, "reversal of an incoming payment", SampleMessages.Reversal(run), checks, cancellationToken);
        return new(checks);
    }

    private static async Task CheckKindAsync(HttpClient client, SampleMessage sample, List<IpsReceiverCheck> checks, CancellationToken token)
    {
        var first = AnswerReader.ReadExchange(await PostAsync(client, sample.Route, sample.Body, sample.Key, token), sample.Echo);
        checks.Add(new($"{sample.Kind}: the answer is final", first.Final, first.Reason));

        var again = AnswerReader.ReadExchange(await PostAsync(client, sample.Route, sample.Body, sample.Key, token), sample.Echo);
        checks.Add(Same($"{sample.Kind}: a redelivery gets the first answer", first, again));

        var query = $"{TransactionRestApiRoutes.PaymentStatus}?messageKind={sample.MessageKind}&reference={Uri.EscapeDataString(sample.Key)}";
        var queried = AnswerReader.ReadExchange(await GetAsync(client, query, token), sample.Echo);
        checks.Add(Same($"{sample.Kind}: the payment-status query returns the answer", first, queried));
    }

    private static async Task CheckUnknownReferenceAsync(HttpClient client, string run, List<IpsReceiverCheck> checks, CancellationToken token)
    {
        var query = $"{TransactionRestApiRoutes.PaymentStatus}?messageKind={IpsMessageKind.Pacs009}&reference=KIT-NEVER-SENT-{run}";
        var exchange = await GetAsync(client, query, token);
        checks.Add(new("the payment-status query of a message never sent answers 404", exchange.Status == 404,
            exchange.Status == 404 ? "404." : $"HTTP {exchange.Status}: only 404 tells the gateway to deliver the message again."));
    }

    private static async Task CheckCallbackAsync(HttpClient client, string name, TransactionStatusDto status, List<IpsReceiverCheck> checks, CancellationToken token)
    {
        var key = IpsIdempotencyKeys.For(status);
        var first = await PostAsync(client, TransactionRestApiRoutes.ReceiveStatus, status, key, token);
        checks.Add(new($"the {name} is accepted", Delivered(first), Describe(first)));
        var again = await PostAsync(client, TransactionRestApiRoutes.ReceiveStatus, status, key, token);
        checks.Add(new($"a redelivered {name} is accepted", Delivered(again), Describe(again)));
    }

    private static IpsReceiverCheck Same(string name, AnswerVerdict first, AnswerVerdict later)
    {
        if (!later.Final)
        {
            return new(name, false, later.Reason);
        }

        return first.Final && later.Status == first.Status
            ? new(name, true, $"{later.Status}, as first answered.")
            : new(name, false, $"{later.Status}, but the first answer was {first.Status ?? "not final"}.");
    }

    private static bool Delivered(Exchange exchange) => exchange.Status is >= 200 and < 300;

    private static string Describe(Exchange exchange) => exchange.Status switch
    {
        0 => exchange.Body,
        >= 200 and < 300 => $"HTTP {exchange.Status}.",
        _ => $"HTTP {exchange.Status}: the gateway retries until any 2xx. {exchange.Body}".Trim()
    };

    private static async Task<Exchange> PostAsync(HttpClient client, string route, object body, string key, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route.TrimStart('/'))
        {
            Content = JsonContent.Create(body, body.GetType(), options: Wire)
        };
        request.Headers.Add(IpsHttpHeaders.IdempotencyKey, key);
        return await SendAsync(client, request, token);
    }

    private static async Task<Exchange> GetAsync(HttpClient client, string pathAndQuery, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery.TrimStart('/'));
        return await SendAsync(client, request, token);
    }

    // A transport failure is recorded as a failed check, not thrown: the run still reports every other check.
    private static async Task<Exchange> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken token)
    {
        try
        {
            using var response = await client.SendAsync(request, token);
            return new((int)response.StatusCode, await response.Content.ReadAsStringAsync(token));
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
        {
            return new(0, string.Format(CultureInfo.InvariantCulture, "No answer: {0}", error.Message));
        }
    }
}

internal sealed record Exchange(int Status, string Body);
