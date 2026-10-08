using System.Diagnostics.Metrics;
using IPS.Middleware.Application.Diagnostics;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Diagnostics;

// Reads what the service's meter reports, so a test can say exactly which measurements one action produced.
internal sealed class MetricsProbe : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<Measured> _measurements = [];
    private readonly Lock _gate = new();

    internal MetricsProbe()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == PaymentMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument.Name, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument.Name, value, tags));
        _listener.Start();
    }

    internal IReadOnlyList<Measured> Of(string instrument)
    {
        lock (_gate)
        {
            return _measurements.Where(measured => measured.Instrument == instrument).ToArray();
        }
    }

    // Counts of one counter by the value of one tag, for compact assertions.
    internal IReadOnlyDictionary<string, long> CountBy(string instrument, string tag) => Of(instrument)
        .GroupBy(measured => measured.Tags[tag]?.ToString() ?? "")
        .ToDictionary(group => group.Key, group => group.Sum(measured => (long)measured.Value));

    internal void Observe() => _listener.RecordObservableInstruments();

    public void Dispose() => _listener.Dispose();

    private void Add(string instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        lock (_gate)
        {
            _measurements.Add(new(instrument, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
        }
    }

    internal sealed record Measured(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);
}

// The meter and the backlog snapshot are process-wide, so every test that counts runs alone.
[CollectionDefinition("Metrics", DisableParallelization = true)]
public sealed class MetricsCollection;
