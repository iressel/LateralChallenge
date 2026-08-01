using CmsSync.Domain.Events;

namespace CmsSync.Domain.Entities;

public sealed record CmsEntityRevisionSnapshot
{
    public CmsEntityRevisionSnapshot(
        string entityId,
        EntityGeneration generation,
        EntityVersion version,
        string firstObservedPayload,
        PayloadHash payloadHash,
        UtcTimestamp firstObservedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstObservedPayload);
        ArgumentNullException.ThrowIfNull(payloadHash);

        if (!generation.IsActive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(generation),
                generation,
                "A payload-bearing revision must have a positive generation.");
        }

        if (!version.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "A payload-bearing revision must have a positive version.");
        }

        EntityId = entityId;
        Generation = generation;
        Version = version;
        FirstObservedPayload = firstObservedPayload;
        PayloadHash = payloadHash;
        FirstObservedAtUtc = firstObservedAtUtc;
    }

    public string EntityId { get; }

    public EntityGeneration Generation { get; }

    public EntityVersion Version { get; }

    public string FirstObservedPayload { get; }

    public PayloadHash PayloadHash { get; }

    public UtcTimestamp FirstObservedAtUtc { get; }
}
