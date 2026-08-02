using System.Security.Cryptography;
using CmsSync.Domain.Entities;
using CmsSync.Domain.Events;

namespace CmsSync.Application.EventIngestion;

public sealed class EventIdentityFactory
{
    internal const string ExternalPrefix = "external:";
    internal const string DerivedPrefix = "sha256:";

    private const byte FormatVersion = 1;
    private const byte VersionAbsent = 0;
    private const byte VersionPresent = 1;
    private const byte PayloadAbsent = 0;
    private const byte PayloadPresent = 1;

    public static EventIdentity Create(
        CmsEventType eventType,
        string entityId,
        EntityVersion? version,
        UtcTimestamp occurredAtUtc,
        ReadOnlyMemory<byte>? canonicalPayload,
        string? eventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ValidateApplicability(eventType, version, canonicalPayload);

        var normalizedContent = CreateNormalizedContent(
            eventType,
            entityId,
            version,
            occurredAtUtc,
            canonicalPayload);
        var hash = new EventContentHash(SHA256.HashData(normalizedContent));
        var key = eventId is null
            ? DerivedPrefix + hash
            : ExternalPrefix + eventId;

        return new EventIdentity(key, hash);
    }

    private static byte[] CreateNormalizedContent(
        CmsEventType eventType,
        string entityId,
        EntityVersion? version,
        UtcTimestamp occurredAtUtc,
        ReadOnlyMemory<byte>? canonicalPayload)
    {
        var writer = new LengthPrefixedEncodingWriter();
        writer.WriteByte(FormatVersion);
        writer.WriteLengthPrefixed(CmsEventTypeNames.GetCanonicalName(eventType));
        writer.WriteLengthPrefixed(entityId);

        if (version is null)
        {
            writer.WriteByte(VersionAbsent);
        }
        else
        {
            writer.WriteByte(VersionPresent);
            writer.WriteInt64(version.Value.Value);
        }

        writer.WriteInt64(occurredAtUtc.Value.UtcDateTime.Ticks);

        if (canonicalPayload is null)
        {
            writer.WriteByte(PayloadAbsent);
        }
        else
        {
            writer.WriteByte(PayloadPresent);
            writer.WriteLengthPrefixed(canonicalPayload.Value.Span);
        }

        return writer.ToArray();
    }

    private static void ValidateApplicability(
        CmsEventType eventType,
        EntityVersion? version,
        ReadOnlyMemory<byte>? canonicalPayload)
    {
        if (eventType == CmsEventType.Delete)
        {
            if (version is not null || canonicalPayload is not null)
            {
                throw new ArgumentException("A delete identity must use the unversioned and no-payload sentinels.");
            }

            return;
        }

        if (version is null || canonicalPayload is null)
        {
            throw new ArgumentException("A versioned event identity requires a version and canonical payload.");
        }
    }
}
