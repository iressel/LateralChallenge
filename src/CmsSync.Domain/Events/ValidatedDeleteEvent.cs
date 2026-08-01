namespace CmsSync.Domain.Events;

public sealed record ValidatedDeleteEvent : ValidatedCmsEvent
{
    public ValidatedDeleteEvent(string entityId, UtcTimestamp occurredAtUtc)
        : base(entityId, occurredAtUtc)
    {
    }
}
