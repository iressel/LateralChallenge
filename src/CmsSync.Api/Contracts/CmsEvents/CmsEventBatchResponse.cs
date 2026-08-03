using System.Text.Json.Serialization;
using CmsSync.Application.EventIngestion;

namespace CmsSync.Api.Contracts.CmsEvents;

public sealed class CmsEventBatchResponse
{
    public CmsEventBatchResponse(CmsEventBatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var responseItems = new CmsEventResultResponse[result.Results.Count];

        for (var index = 0; index < result.Results.Count; index++)
        {
            responseItems[index] = new CmsEventResultResponse(result.Results[index]);
        }

        BatchId = result.BatchId;
        Results = Array.AsReadOnly(responseItems);
        Summary = new CmsEventSummaryResponse(result.Summary);
    }

    [JsonPropertyName("batchId")]
    public Guid BatchId { get; }

    [JsonPropertyName("results")]
    public IReadOnlyList<CmsEventResultResponse> Results { get; }

    [JsonPropertyName("summary")]
    public CmsEventSummaryResponse Summary { get; }
}
