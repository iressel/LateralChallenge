using CmsSync.Domain.Entities;
using CmsSync.Domain.Events;

namespace CmsSync.Application.EventIngestion;

public sealed class InvalidCmsEventIdentityData
{
    internal InvalidCmsEventIdentityData(
        string? eventId,
        CmsEventType eventType,
        string entityId,
        EntityVersion version,
        UtcTimestamp occurredAtUtc,
        PayloadHash payloadHash,
        EventIdentity identity)
    {
        EventId = eventId;
        EventType = eventType;
        EntityId = entityId;
        Version = version;
        OccurredAtUtc = occurredAtUtc;
        PayloadHash = payloadHash;
        IdempotencyKey = identity.IdempotencyKey;
        EventContentHash = identity.ContentHash;
    }

    public string? EventId { get; }

    public CmsEventType EventType { get; }

    public string CanonicalEventType => CmsEventTypeNames.GetCanonicalName(EventType);

    public string EntityId { get; }

    public EntityVersion Version { get; }

    public UtcTimestamp OccurredAtUtc { get; }

    public PayloadHash PayloadHash { get; }

    public string IdempotencyKey { get; }

    public EventContentHash EventContentHash { get; }
}
