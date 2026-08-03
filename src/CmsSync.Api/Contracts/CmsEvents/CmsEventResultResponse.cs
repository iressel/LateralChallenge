using System.Text.Json.Serialization;
using CmsSync.Application.EventIngestion;
using CmsSync.Domain.Processing;

namespace CmsSync.Api.Contracts.CmsEvents;

public sealed class CmsEventResultResponse
{
    public CmsEventResultResponse(CmsEventBatchItemResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Sequence = result.Sequence;
        EventId = result.EventId;
        Id = result.EntityId;
        Outcome = GetOutcomeName(result.Outcome);
        Code = result.Code;
        Generation = result.Generation;
        ResultingVersion = result.ResultingVersion;
    }

    [JsonPropertyName("sequence")]
    public int Sequence { get; }

    [JsonPropertyName("eventId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EventId { get; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; }

    [JsonPropertyName("outcome")]
    public string Outcome { get; }

    [JsonPropertyName("code")]
    public string Code { get; }

    [JsonPropertyName("generation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Generation { get; }

    [JsonPropertyName("resultingVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ResultingVersion { get; }

    private static string GetOutcomeName(ProcessingOutcome outcome)
    {
        return outcome switch
        {
            ProcessingOutcome.Applied => "applied",
            ProcessingOutcome.Duplicate => "duplicate",
            ProcessingOutcome.Equivalent => "equivalent",
            ProcessingOutcome.Stale => "stale",
            ProcessingOutcome.Invalid => "invalid",
            ProcessingOutcome.Conflict => "conflict",
            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "The processing outcome is not supported by the webhook contract."),
        };
    }
}
