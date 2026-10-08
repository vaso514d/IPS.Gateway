using System.Text.RegularExpressions;
using Aspire.Hosting.ApplicationModel;

namespace IPS.Middleware.Performance;

// A group of an instance's warnings or errors: the normalised template (category and first message line, at most 80 characters),
// how often it was written during the run, and the first such entry as written with its first exception line.
internal sealed record LogTemplate(string Level, string Template, int Count, string Sample);

internal sealed record InstanceLogSummary(int Instance, IReadOnlyDictionary<string, int> EntriesByLevel, IReadOnlyList<LogTemplate> Top);

// What one API instance logged during the run, read from the orchestrator's log stream (ResourceLoggerService). The stream first
// replays what the instance wrote before; those lines are skipped by line number. Each line carries the orchestrator's timestamp,
// and the console's simple format writes an entry as a coloured "level: category[event]" followed by indented message lines.
// Counting runs on the stream's own task while the summary is read from the run, hence the lock.
internal sealed partial class InstanceLogs
{
    private const int TopTemplates = 10;
    private const int TemplateLength = 80;
    private const int SampleLength = 300;
    private readonly object _gate = new();
    private readonly int _instance;
    private readonly Dictionary<string, int> _levels = [];
    private readonly Dictionary<string, LogTemplate> _templates = [];
    private string? _level;
    private string? _category;
    private string? _entry;
    private bool _exceptionSeen;
    private bool _stopped;

    private InstanceLogs(int instance)
    {
        _instance = instance;
    }

    internal static async Task<InstanceLogs> StartAsync(ResourceLoggerService logs, string resource, int instance, CancellationToken cancellationToken)
    {
        var lastBefore = 0;
        await foreach (var batch in logs.GetAllAsync(resource).WithCancellation(cancellationToken))
        {
            lastBefore = batch.Count == 0 ? lastBefore : Math.Max(lastBefore, batch.Max(line => line.LineNumber));
        }

        var collector = new InstanceLogs(instance);
        _ = collector.ReadAsync(logs.WatchAsync(resource), lastBefore);
        return collector;
    }

    internal InstanceLogSummary Stop()
    {
        lock (_gate)
        {
            _stopped = true;
            var top = _templates.Values
                .OrderByDescending(template => template.Count)
                .ThenBy(template => template.Template, StringComparer.Ordinal)
                .Take(TopTemplates)
                .ToArray();
            var levels = _levels
                .OrderByDescending(level => level.Value)
                .ToDictionary(level => level.Key, level => level.Value, StringComparer.Ordinal);
            return new InstanceLogSummary(_instance, levels, top);
        }
    }

    // The stream ends when the instance stops, which is after the summary was taken; a failure of the stream only ends counting.
    private async Task ReadAsync(IAsyncEnumerable<IReadOnlyList<LogLine>> stream, int lastBefore)
    {
        try
        {
            await foreach (var batch in stream)
            {
                lock (_gate)
                {
                    if (_stopped)
                    {
                        return;
                    }

                    foreach (var line in batch.Where(line => line.LineNumber > lastBefore))
                    {
                        Read(line.Content);
                    }
                }
            }
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            // The stack is shutting down.
        }
    }

    private void Read(string content)
    {
        var line = Timestamp().Replace(Colour().Replace(content, ""), "", 1);
        var header = Header().Match(line);
        if (header.Success)
        {
            _level = header.Groups["level"].Value;
            _category = header.Groups["category"].Value;
            _entry = null;
            _levels[_level] = _levels.GetValueOrDefault(_level) + 1;
            return;
        }

        if (_level is not ("warn" or "fail" or "crit") || _category is null)
        {
            return;
        }

        // The first message line names the entry; of the later lines only the first exception line is kept, in the first sample.
        if (_entry is null)
        {
            _entry = Add(_level, _category + ": " + line.Trim());
            _exceptionSeen = false;
            return;
        }

        if (!_exceptionSeen && line.Contains("Exception", StringComparison.Ordinal))
        {
            _exceptionSeen = true;
            var template = _templates[_entry];
            if (template.Count == 1)
            {
                _templates[_entry] = template with { Sample = Cut(template.Sample + " | " + line.Trim()) };
            }
        }
    }

    private string Add(string level, string entry)
    {
        var template = Normalise(entry);
        var key = level + " " + template;
        _templates[key] = _templates.TryGetValue(key, out var known)
            ? known with { Count = known.Count + 1 }
            : new LogTemplate(level, template, 1, Cut(entry));
        return key;
    }

    private static string Cut(string text) => text[..Math.Min(text.Length, SampleLength)];

    // Identifiers and numbers vary per payment; without them the same message groups together.
    private static string Normalise(string entry)
    {
        var template = Guid().Replace(entry, "{id}");
        template = Reference().Replace(template, "{reference}");
        template = Digits().Replace(template, "#");
        template = Spaces().Replace(template, " ");
        return template[..Math.Min(template.Length, TemplateLength)];
    }

    // The level and the category without its event id.
    [GeneratedRegex(@"^(?<level>trce|dbug|info|warn|fail|crit): (?<category>[^\[]+)")]
    private static partial Regex Header();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T[\d:.]+Z ")]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Colour();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Guid();

    [GeneratedRegex(@"perf-[0-9a-f]{12}")]
    private static partial Regex Reference();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
