using CmsSync.Domain.Entities;

namespace CmsSync.Domain.Events;

public sealed record ValidatedUnpublishEvent : ValidatedCmsEvent
{
    public ValidatedUnpublishEvent(
        string entityId,
        EntityVersion version,
        UtcTimestamp occurredAtUtc,
        string payload,
        PayloadHash payloadHash)
        : base(entityId, occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentNullException.ThrowIfNull(payloadHash);

        if (!version.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "A validated unpublish event must have a positive version.");
        }

        Version = version;
        Payload = payload;
        PayloadHash = payloadHash;
    }

    public EntityVersion Version { get; }

    public string Payload { get; }

    public PayloadHash PayloadHash { get; }
}
