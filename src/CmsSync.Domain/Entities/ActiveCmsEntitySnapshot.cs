using CmsSync.Domain.Events;

namespace CmsSync.Domain.Entities;

public sealed record ActiveCmsEntitySnapshot
{
    public ActiveCmsEntitySnapshot(
        string entityId,
        EntityGeneration generation,
        EntityVersion latestVersion,
        string payload,
        PayloadHash payloadHash,
        CmsPublicationStatus publicationStatus,
        UtcTimestamp currentVersionOccurredAtUtc,
        UtcTimestamp entityEventHighWatermarkUtc,
        bool administrativeDisabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentNullException.ThrowIfNull(payloadHash);

        if (!generation.IsActive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(generation),
                generation,
                "An active entity must have a positive generation.");
        }

        if (!latestVersion.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(latestVersion),
                latestVersion,
                "An active entity must have a positive version.");
        }

        if (!Enum.IsDefined(publicationStatus))
        {
            throw new ArgumentOutOfRangeException(
                nameof(publicationStatus),
                publicationStatus,
                "The CMS publication status is not supported.");
        }

        if (entityEventHighWatermarkUtc < currentVersionOccurredAtUtc)
        {
            throw new ArgumentException(
                "The entity event high watermark cannot precede the current version timestamp.",
                nameof(entityEventHighWatermarkUtc));
        }

        EntityId = entityId;
        Generation = generation;
        LatestVersion = latestVersion;
        Payload = payload;
        PayloadHash = payloadHash;
        PublicationStatus = publicationStatus;
        CurrentVersionOccurredAtUtc = currentVersionOccurredAtUtc;
        EntityEventHighWatermarkUtc = entityEventHighWatermarkUtc;
        AdministrativeDisabled = administrativeDisabled;
    }

    public string EntityId { get; }

    public EntityGeneration Generation { get; }

    public EntityVersion LatestVersion { get; }

    public string Payload { get; }

    public PayloadHash PayloadHash { get; }

    public CmsPublicationStatus PublicationStatus { get; }

    public UtcTimestamp CurrentVersionOccurredAtUtc { get; }

    public UtcTimestamp EntityEventHighWatermarkUtc { get; }

    public bool AdministrativeDisabled { get; }
}
