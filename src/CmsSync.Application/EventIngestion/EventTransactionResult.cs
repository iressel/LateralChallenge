using CmsSync.Domain.Processing;

namespace CmsSync.Application.EventIngestion;

public sealed class EventTransactionResult
{
    public EventTransactionResult(
        int sequence,
        string? eventId,
        string? entityId,
        ProcessingOutcome outcome,
        string code,
        long? generation = null,
        long? resultingVersion = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The processing outcome is not supported.");
        }

        if (generation < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), generation, "Generation cannot be negative.");
        }

        if (resultingVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resultingVersion),
                resultingVersion,
                "A resulting version must be positive when supplied.");
        }

        Sequence = sequence;
        EventId = eventId;
        EntityId = entityId;
        Outcome = outcome;
        Code = code;
        Generation = generation;
        ResultingVersion = resultingVersion;
    }

    public int Sequence { get; }

    public string? EventId { get; }

    public string? EntityId { get; }

    public ProcessingOutcome Outcome { get; }

    public string Code { get; }

    public long? Generation { get; }

    public long? ResultingVersion { get; }
}
