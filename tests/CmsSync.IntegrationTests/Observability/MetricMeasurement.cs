namespace CmsSync.IntegrationTests.Observability;

internal sealed class MetricMeasurement
{
    public MetricMeasurement(
        string instrumentName,
        double value,
        IReadOnlyDictionary<string, object?> tags)
    {
        InstrumentName = instrumentName;
        Value = value;
        Tags = tags;
    }

    public string InstrumentName { get; }

    public double Value { get; }

    public IReadOnlyDictionary<string, object?> Tags { get; }
}
