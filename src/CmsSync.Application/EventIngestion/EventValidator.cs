using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CmsSync.Domain.Entities;
using CmsSync.Domain.Events;

namespace CmsSync.Application.EventIngestion;

public sealed class EventValidator
{
    private const int MaximumIdentifierLength = 200;

    private static readonly string[] TimestampWithoutOffsetFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
    ];

    private static readonly string[] TimestampWithOffsetFormats =
    [
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
    ];

    private readonly CmsEventIngestionLimits _limits;

    public EventValidator(CmsEventIngestionLimits? limits = null)
    {
        _limits = limits ?? new CmsEventIngestionLimits();
    }

    public EventValidationResult Validate(ParsedCmsEventItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.HasDuplicatePropertyNames)
        {
            return Invalid(
                item,
                EventValidationCodes.DuplicatePropertyName,
                "The event contains a duplicate JSON property name.");
        }

        using var document = ParseItem(item);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return Invalid(
                item,
                EventValidationCodes.EventMustBeObject,
                "Each event item must be a JSON object.");
        }

        var properties = ReadKnownProperties(root);

        if (!TryReadEventType(properties.Type, out var eventType, out var typeCode))
        {
            return Invalid(item, typeCode, "The event type is missing or unsupported.");
        }

        if (!TryReadRequiredIdentifier(properties.EntityId, out var entityId, out var entityIdCode))
        {
            return Invalid(item, entityIdCode, "The id field is missing or invalid.");
        }

        if (!TryReadOptionalEventId(properties.EventId, out var eventId))
        {
            return Invalid(
                item,
                EventValidationCodes.EventIdInvalid,
                "The eventId field is invalid.");
        }

        if (!TryReadTimestamp(properties.Timestamp, out var occurredAtUtc, out var timestampCode))
        {
            return Invalid(item, timestampCode, "The timestamp field is missing or invalid.");
        }

        return eventType == CmsEventType.Delete
            ? ValidateDelete(item, properties, eventId, entityId, occurredAtUtc)
            : ValidateVersioned(item, properties, eventId, eventType, entityId, occurredAtUtc);
    }

    private static EventValidationResult ValidateDelete(
        ParsedCmsEventItem item,
        KnownEventProperties properties,
        string? eventId,
        string entityId,
        UtcTimestamp occurredAtUtc)
    {
        if (properties.Version is not null)
        {
            return Invalid(
                item,
                EventValidationCodes.VersionNotAllowed,
                "A delete event must not contain version.");
        }

        if (properties.Payload is not null)
        {
            return Invalid(
                item,
                EventValidationCodes.PayloadNotAllowed,
                "A delete event must not contain payload.");
        }

        var identity = EventIdentityFactory.Create(
            CmsEventType.Delete,
            entityId,
            version: null,
            occurredAtUtc,
            canonicalPayload: null,
            eventId);
        var domainEvent = new ValidatedDeleteEvent(entityId, occurredAtUtc);

        return EventValidationResult.Valid(
            new ValidatedCmsEventData(
                item.Sequence,
                eventId,
                CmsEventType.Delete,
                entityId,
                version: null,
                occurredAtUtc,
                rawPayload: null,
                payloadHash: null,
                identity,
                domainEvent));
    }

    private EventValidationResult ValidateVersioned(
        ParsedCmsEventItem item,
        KnownEventProperties properties,
        string? eventId,
        CmsEventType eventType,
        string entityId,
        UtcTimestamp occurredAtUtc)
    {
        if (!TryReadVersion(properties.Version, out var version, out var versionCode))
        {
            return Invalid(item, versionCode, "The version field is missing or invalid.");
        }

        if (properties.Payload is null)
        {
            return Invalid(
                item,
                EventValidationCodes.PayloadRequired,
                "A publish or unpublish event requires payload.");
        }

        var payloadElement = properties.Payload.Value;

        if (payloadElement.ValueKind != JsonValueKind.Object)
        {
            return Invalid(
                item,
                EventValidationCodes.PayloadMustBeObject,
                "Payload must be a JSON object.");
        }

        var rawPayload = payloadElement.GetRawText();

        if (Encoding.UTF8.GetByteCount(rawPayload) > _limits.MaximumPayloadSizeBytes)
        {
            return Invalid(
                item,
                EventValidationCodes.PayloadTooLarge,
                "Payload exceeds the configured byte limit.");
        }

        var canonicalPayload = CanonicalJson.Canonicalize(payloadElement);
        var payloadHash = new PayloadHash(SHA256.HashData(canonicalPayload));
        var identity = EventIdentityFactory.Create(
            eventType,
            entityId,
            version,
            occurredAtUtc,
            canonicalPayload,
            eventId);

        ValidatedCmsEvent domainEvent = eventType switch
        {
            CmsEventType.Publish => new ValidatedPublishEvent(
                entityId,
                version,
                occurredAtUtc,
                rawPayload,
                payloadHash),
            CmsEventType.Unpublish => new ValidatedUnpublishEvent(
                entityId,
                version,
                occurredAtUtc,
                rawPayload,
                payloadHash),
            _ => throw new InvalidOperationException("A versioned event has an unexpected normalized type."),
        };

        return EventValidationResult.Valid(
            new ValidatedCmsEventData(
                item.Sequence,
                eventId,
                eventType,
                entityId,
                version,
                occurredAtUtc,
                rawPayload,
                payloadHash,
                identity,
                domainEvent));
    }

    private JsonDocument ParseItem(ParsedCmsEventItem item)
    {
        try
        {
            return JsonDocument.Parse(
                item.Utf8Json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = _limits.MaximumJsonDepth,
                });
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "A parsed event item was not valid JSON within the configured depth.",
                exception);
        }
    }

    private static KnownEventProperties ReadKnownProperties(JsonElement root)
    {
        JsonElement? eventId = null;
        JsonElement? type = null;
        JsonElement? entityId = null;
        JsonElement? version = null;
        JsonElement? timestamp = null;
        JsonElement? payload = null;

        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "eventId":
                    eventId = property.Value;
                    break;
                case "type":
                    type = property.Value;
                    break;
                case "id":
                    entityId = property.Value;
                    break;
                case "version":
                    version = property.Value;
                    break;
                case "timestamp":
                    timestamp = property.Value;
                    break;
                case "payload":
                    payload = property.Value;
                    break;
            }
        }

        return new KnownEventProperties(eventId, type, entityId, version, timestamp, payload);
    }

    private static bool TryReadEventType(
        JsonElement? element,
        out CmsEventType eventType,
        out string failureCode)
    {
        eventType = default;

        if (element is null)
        {
            failureCode = EventValidationCodes.EventTypeRequired;
            return false;
        }

        if (element.Value.ValueKind != JsonValueKind.String)
        {
            failureCode = EventValidationCodes.EventTypeInvalid;
            return false;
        }

        var value = element.Value.GetString()?.Trim();

        if (string.Equals(value, CmsEventTypeNames.Publish, StringComparison.OrdinalIgnoreCase))
        {
            eventType = CmsEventType.Publish;
        }
        else if (string.Equals(value, CmsEventTypeNames.Unpublish, StringComparison.OrdinalIgnoreCase))
        {
            eventType = CmsEventType.Unpublish;
        }
        else if (string.Equals(value, CmsEventTypeNames.Delete, StringComparison.OrdinalIgnoreCase))
        {
            eventType = CmsEventType.Delete;
        }
        else
        {
            failureCode = EventValidationCodes.EventTypeInvalid;
            return false;
        }

        failureCode = string.Empty;
        return true;
    }

    private static bool TryReadRequiredIdentifier(
        JsonElement? element,
        out string entityId,
        out string failureCode)
    {
        entityId = string.Empty;

        if (element is null)
        {
            failureCode = EventValidationCodes.EntityIdRequired;
            return false;
        }

        if (!TryReadBoundedString(element.Value, out entityId))
        {
            failureCode = EventValidationCodes.EntityIdInvalid;
            return false;
        }

        failureCode = string.Empty;
        return true;
    }

    private static bool TryReadOptionalEventId(JsonElement? element, out string? eventId)
    {
        eventId = null;

        if (element is null)
        {
            return true;
        }

        if (!TryReadBoundedString(element.Value, out var value))
        {
            return false;
        }

        eventId = value;
        return true;
    }

    private static bool TryReadBoundedString(JsonElement element, out string value)
    {
        value = string.Empty;

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var parsed = element.GetString();

        if (string.IsNullOrEmpty(parsed) ||
            parsed.Length > MaximumIdentifierLength ||
            parsed.Length != parsed.Trim().Length ||
            parsed.Any(char.IsControl))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadTimestamp(
        JsonElement? element,
        out UtcTimestamp timestamp,
        out string failureCode)
    {
        timestamp = default;

        if (element is null)
        {
            failureCode = EventValidationCodes.TimestampRequired;
            return false;
        }

        if (element.Value.ValueKind != JsonValueKind.String ||
            !TryParseTimestamp(element.Value.GetString(), out var parsedTimestamp))
        {
            failureCode = EventValidationCodes.TimestampInvalid;
            return false;
        }

        timestamp = new UtcTimestamp(parsedTimestamp.ToUniversalTime());
        failureCode = string.Empty;
        return true;
    }

    private static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;

        if (value is null || value.Length < 20 || value[10] != 'T')
        {
            return false;
        }

        if (value.EndsWith('Z'))
        {
            var timestampText = value[..^1];

            if (!HasValidFractionalSecondShape(timestampText) ||
                !DateTime.TryParseExact(
                    timestampText,
                    TimestampWithoutOffsetFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var utcValue))
            {
                return false;
            }

            timestamp = new DateTimeOffset(
                DateTime.SpecifyKind(utcValue, DateTimeKind.Unspecified),
                TimeSpan.Zero);
            return true;
        }

        if (!HasExplicitOffset(value) || !HasValidFractionalSecondShape(value[..^6]))
        {
            return false;
        }

        return DateTimeOffset.TryParseExact(
            value,
            TimestampWithOffsetFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out timestamp);
    }

    private static bool HasExplicitOffset(string value)
    {
        if (value.Length < 25)
        {
            return false;
        }

        var offsetStart = value.Length - 6;

        return value[offsetStart] is '+' or '-' &&
               value[offsetStart + 3] == ':' &&
               char.IsAsciiDigit(value[offsetStart + 1]) &&
               char.IsAsciiDigit(value[offsetStart + 2]) &&
               char.IsAsciiDigit(value[offsetStart + 4]) &&
               char.IsAsciiDigit(value[offsetStart + 5]);
    }

    private static bool HasValidFractionalSecondShape(string timestampWithoutOffset)
    {
        if (timestampWithoutOffset.Length == 19)
        {
            return true;
        }

        if (timestampWithoutOffset.Length is < 21 or > 27 || timestampWithoutOffset[19] != '.')
        {
            return false;
        }

        return timestampWithoutOffset.AsSpan(20).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    private static bool TryReadVersion(
        JsonElement? element,
        out EntityVersion version,
        out string failureCode)
    {
        version = default;

        if (element is null)
        {
            failureCode = EventValidationCodes.VersionRequired;
            return false;
        }

        if (element.Value.ValueKind != JsonValueKind.Number)
        {
            failureCode = EventValidationCodes.VersionInvalid;
            return false;
        }

        var rawValue = element.Value.GetRawText();

        if (rawValue.AsSpan().IndexOfAnyExceptInRange('0', '9') >= 0 ||
            !long.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedValue) ||
            parsedValue <= 0)
        {
            failureCode = EventValidationCodes.VersionInvalid;
            return false;
        }

        version = new EntityVersion(parsedValue);
        failureCode = string.Empty;
        return true;
    }

    private static EventValidationResult Invalid(
        ParsedCmsEventItem item,
        string code,
        string message)
    {
        return EventValidationResult.Invalid(item.Sequence, code, message);
    }

    private sealed record KnownEventProperties(
        JsonElement? EventId,
        JsonElement? Type,
        JsonElement? EntityId,
        JsonElement? Version,
        JsonElement? Timestamp,
        JsonElement? Payload);
}
