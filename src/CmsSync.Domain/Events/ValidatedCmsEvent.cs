namespace CmsSync.Domain.Events;

public abstract record ValidatedCmsEvent
{
    private protected ValidatedCmsEvent(string entityId, UtcTimestamp occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        EntityId = entityId;
        OccurredAtUtc = occurredAtUtc;
    }

    public string EntityId { get; }

    public UtcTimestamp OccurredAtUtc { get; }
}
