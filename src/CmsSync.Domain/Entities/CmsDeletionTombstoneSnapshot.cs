using CmsSync.Domain.Events;

namespace CmsSync.Domain.Entities;

public sealed record CmsDeletionTombstoneSnapshot
{
    public CmsDeletionTombstoneSnapshot(
        string entityId,
        EntityGeneration lastDeletedGeneration,
        UtcTimestamp deletedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        EntityId = entityId;
        LastDeletedGeneration = lastDeletedGeneration;
        DeletedAtUtc = deletedAtUtc;
    }

    public string EntityId { get; }

    public EntityGeneration LastDeletedGeneration { get; }

    public UtcTimestamp DeletedAtUtc { get; }
}
