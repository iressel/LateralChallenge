using System.Text.Json.Serialization;
using CmsSync.Application.EventIngestion;

namespace CmsSync.Api.Contracts.CmsEvents;

public sealed class CmsEventSummaryResponse
{
    public CmsEventSummaryResponse(CmsEventBatchSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Total = summary.Total;
        Applied = summary.Applied;
        Duplicate = summary.Duplicate;
        Equivalent = summary.Equivalent;
        Stale = summary.Stale;
        Invalid = summary.Invalid;
        Conflict = summary.Conflict;
    }

    [JsonPropertyName("total")]
    public int Total { get; }

    [JsonPropertyName("applied")]
    public int Applied { get; }

    [JsonPropertyName("duplicate")]
    public int Duplicate { get; }

    [JsonPropertyName("equivalent")]
    public int Equivalent { get; }

    [JsonPropertyName("stale")]
    public int Stale { get; }

    [JsonPropertyName("invalid")]
    public int Invalid { get; }

    [JsonPropertyName("conflict")]
    public int Conflict { get; }
}
