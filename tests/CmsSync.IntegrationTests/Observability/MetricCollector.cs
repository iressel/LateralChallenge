using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using CmsSync.Application.Observability;

namespace CmsSync.IntegrationTests.Observability;

internal sealed class MetricCollector : IDisposable
{
    private readonly ConcurrentQueue<MetricMeasurement> _measurements = new();
    private readonly MeterListener _listener = new();

    public MetricCollector()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (string.Equals(instrument.Meter.Name, CmsOperationalMetrics.MeterName, StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument, this);
            }
        };
        _listener.SetMeasurementEventCallback<long>(RecordMeasurement);
        _listener.SetMeasurementEventCallback<double>(RecordMeasurement);
        _listener.Start();
    }

    public IReadOnlyCollection<MetricMeasurement> Measurements
    {
        get
        {
            return _measurements.ToArray();
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    private void RecordMeasurement<T>(
        Instrument instrument,
        T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
        where T : struct
    {
        var copiedTags = tags.ToArray().ToDictionary(
            tag => tag.Key,
            tag => tag.Value,
            StringComparer.Ordinal);
        _measurements.Enqueue(new MetricMeasurement(
            instrument.Name,
            Convert.ToDouble(measurement, System.Globalization.CultureInfo.InvariantCulture),
            copiedTags));
    }
}
