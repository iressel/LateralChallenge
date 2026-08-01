using CmsSync.Domain.Entities;
using CmsSync.Domain.Events;

namespace CmsSync.UnitTests.TestSupport;

internal static class CmsStateTestData
{
    public const string EntityId = "entity-1";

    public const string Payload = "{\"name\":\"value\"}";

    public const string DifferentPayload = "{\"name\":\"different\"}";

    public static UtcTimestamp At(int hour, int minute = 0, int second = 0) =>
        new(new DateTimeOffset(2026, 7, 31, hour, minute, second, TimeSpan.Zero));

    public static PayloadHash Hash(byte value = 1) =>
        new(Enumerable.Repeat(value, PayloadHash.Length).ToArray());

    public static ActiveCmsEntitySnapshot Active(
        long generation = 1,
        long version = 5,
        CmsPublicationStatus status = CmsPublicationStatus.Published,
        UtcTimestamp? currentVersionOccurredAtUtc = null,
        UtcTimestamp? entityEventHighWatermarkUtc = null,
        bool administrativeDisabled = false,
        string entityId = EntityId,
        string payload = Payload,
        PayloadHash? payloadHash = null)
    {
        var currentTimestamp = currentVersionOccurredAtUtc ?? At(10);

        return new ActiveCmsEntitySnapshot(
            entityId,
            new EntityGeneration(generation),
            new EntityVersion(version),
            payload,
            payloadHash ?? Hash(),
            status,
            currentTimestamp,
            entityEventHighWatermarkUtc ?? currentTimestamp,
            administrativeDisabled);
    }

    public static CmsEntityRevisionSnapshot Revision(
        long generation = 1,
        long version = 5,
        string entityId = EntityId,
        string payload = Payload,
        PayloadHash? payloadHash = null,
        UtcTimestamp? firstObservedAtUtc = null) =>
        new(
            entityId,
            new EntityGeneration(generation),
            new EntityVersion(version),
            payload,
            payloadHash ?? Hash(),
            firstObservedAtUtc ?? At(10));

    public static CmsDeletionTombstoneSnapshot Tombstone(
        long lastDeletedGeneration = 1,
        UtcTimestamp? deletedAtUtc = null,
        string entityId = EntityId) =>
        new(
            entityId,
            new EntityGeneration(lastDeletedGeneration),
            deletedAtUtc ?? At(8));

    public static ValidatedCmsEvent VersionedEvent(
        CmsPublicationStatus status = CmsPublicationStatus.Published,
        long version = 5,
        UtcTimestamp? occurredAtUtc = null,
        string entityId = EntityId,
        string payload = Payload,
        PayloadHash? payloadHash = null)
    {
        var timestamp = occurredAtUtc ?? At(10);
        var hash = payloadHash ?? Hash();

        return status == CmsPublicationStatus.Published
            ? new ValidatedPublishEvent(entityId, new EntityVersion(version), timestamp, payload, hash)
            : new ValidatedUnpublishEvent(entityId, new EntityVersion(version), timestamp, payload, hash);
    }

    public static ValidatedDeleteEvent Delete(
        UtcTimestamp? occurredAtUtc = null,
        string entityId = EntityId) =>
        new(entityId, occurredAtUtc ?? At(11));
}
