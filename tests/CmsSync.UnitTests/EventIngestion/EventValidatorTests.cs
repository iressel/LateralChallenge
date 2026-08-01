using CmsSync.Application.EventIngestion;
using CmsSync.Domain.Events;
using Xunit;

namespace CmsSync.UnitTests.EventIngestion;

public sealed class EventValidatorTests
{
    [Fact]
    public void AC013WireIdIsAcceptedMappedAndPreservedExactly()
    {
        var validated = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(idProperty: "\"id\":\"Entity-Aa\""));

        Assert.Equal("Entity-Aa", validated.EntityId);
        Assert.Equal("Entity-Aa", validated.DomainEvent.EntityId);
    }

    [Theory]
    [InlineData("\"entityId\":\"entity-1\"")]
    [InlineData("\"Id\":\"entity-1\"")]
    [InlineData("\"ID\":\"entity-1\"")]
    public void AC010EntityIdAliasesAreIgnoredAndWireIdRemainsRequired(string idProperty)
    {
        var result = EventIngestionTestHelper.ValidateSingle(
            EventIngestionTestHelper.Publish(idProperty: idProperty));

        Assert.Equal(EventValidationCodes.EntityIdRequired, result.Failure?.Code);
        Assert.Null(result.ValidatedEvent);
    }

    [Theory]
    [InlineData("{\"Type\":\"publish\",\"id\":\"entity-1\",\"version\":1,\"timestamp\":\"2026-07-31T12:34:56Z\",\"payload\":{}}", "EVENT_TYPE_REQUIRED")]
    [InlineData("{\"type\":\"publish\",\"Id\":\"entity-1\",\"version\":1,\"timestamp\":\"2026-07-31T12:34:56Z\",\"payload\":{}}", "ENTITY_ID_REQUIRED")]
    [InlineData("{\"type\":\"publish\",\"id\":\"entity-1\",\"Version\":1,\"timestamp\":\"2026-07-31T12:34:56Z\",\"payload\":{}}", "VERSION_REQUIRED")]
    [InlineData("{\"type\":\"publish\",\"id\":\"entity-1\",\"version\":1,\"Timestamp\":\"2026-07-31T12:34:56Z\",\"payload\":{}}", "TIMESTAMP_REQUIRED")]
    [InlineData("{\"type\":\"publish\",\"id\":\"entity-1\",\"version\":1,\"timestamp\":\"2026-07-31T12:34:56Z\",\"Payload\":{}}", "PAYLOAD_REQUIRED")]
    public void RequiredKnownPropertyNamesAreCaseSensitive(string eventJson, string expectedCode)
    {
        var result = EventIngestionTestHelper.ValidateSingle(eventJson);

        Assert.Equal(expectedCode, result.Failure?.Code);
    }

    [Fact]
    public void IncorrectlyCasedOptionalEventIdIsIgnored()
    {
        var validated = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(eventIdProperty: "\"EventId\":\"external-value\""));

        Assert.Null(validated.EventId);
        Assert.StartsWith("sha256:", validated.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public void AC011UnknownEnvelopePropertiesAreIgnored()
    {
        var baseline = EventIngestionTestHelper.ValidateValid(EventIngestionTestHelper.Publish());
        var withUnknown = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(unknownProperty: "\"unknown\":{\"value\":42}"));

        Assert.Equal(baseline.EventContentHash, withUnknown.EventContentHash);
        Assert.Equal(baseline.IdempotencyKey, withUnknown.IdempotencyKey);
    }

    [Theory]
    [InlineData("publish", "publish")]
    [InlineData("Publish", "publish")]
    [InlineData("PUBLISH", "publish")]
    [InlineData("unpublish", "unpublish")]
    [InlineData("unPublish", "unpublish")]
    [InlineData("UnPublish", "unpublish")]
    [InlineData("UNPUBLISH", "unpublish")]
    [InlineData("delete", "delete")]
    [InlineData("Delete", "delete")]
    [InlineData("DELETE", "delete")]
    [InlineData("  publish\t", "publish")]
    [InlineData("\r\nUnPublish  ", "unpublish")]
    [InlineData(" DELETE ", "delete")]
    public void AC009DocumentedEventTypeVariantsNormalizeToCanonicalLowercase(
        string suppliedType,
        string expectedCanonicalType)
    {
        var eventJson = expectedCanonicalType == "delete"
            ? EventIngestionTestHelper.Delete(suppliedType)
            : EventIngestionTestHelper.Publish(suppliedType);

        var validated = EventIngestionTestHelper.ValidateValid(eventJson);

        Assert.Equal(expectedCanonicalType, validated.CanonicalEventType);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("")]
    [InlineData("   ")]
    public void UnsupportedEventTypesAreIndividuallyInvalid(string eventType)
    {
        var result = EventIngestionTestHelper.ValidateSingle(
            EventIngestionTestHelper.Publish(eventType));

        Assert.Equal(EventValidationCodes.EventTypeInvalid, result.Failure?.Code);
    }

    [Fact]
    public void NonStringEventTypeIsInvalid()
    {
        var eventJson = "{\"type\":1,\"id\":\"entity-1\",\"version\":1,\"timestamp\":\"2026-07-31T12:34:56Z\",\"payload\":{}}";

        var result = EventIngestionTestHelper.ValidateSingle(eventJson);

        Assert.Equal(EventValidationCodes.EventTypeInvalid, result.Failure?.Code);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"value\"")]
    [InlineData("[]")]
    public void AC010NonObjectItemsAreIndividuallyInvalid(string itemJson)
    {
        var result = EventIngestionTestHelper.ValidateSingle(itemJson);

        Assert.Equal(EventValidationCodes.EventMustBeObject, result.Failure?.Code);
    }

    [Theory]
    [InlineData("\"id\":\"\"", "ENTITY_ID_INVALID")]
    [InlineData("\"id\":\" entity\"", "ENTITY_ID_INVALID")]
    [InlineData("\"id\":\"entity \"", "ENTITY_ID_INVALID")]
    [InlineData("\"id\":\"entity\\u0001\"", "ENTITY_ID_INVALID")]
    [InlineData("\"id\":null", "ENTITY_ID_INVALID")]
    [InlineData("\"id\":42", "ENTITY_ID_INVALID")]
    public void InvalidEntityIdsAreRejected(string idProperty, string expectedCode)
    {
        var result = EventIngestionTestHelper.ValidateSingle(
            EventIngestionTestHelper.Publish(idProperty: idProperty));

        Assert.Equal(expectedCode, result.Failure?.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void EntityIdLengthBoundsAreAccepted(int length)
    {
        var id = new string('a', length);

        var validated = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(idProperty: $"\"id\":\"{id}\""));

        Assert.Equal(id, validated.EntityId);
    }

    [Fact]
    public void EntityIdAboveMaximumLengthIsRejected()
    {
        var id = new string('a', 201);

        var result = EventIngestionTestHelper.ValidateSingle(
            EventIngestionTestHelper.Publish(idProperty: $"\"id\":\"{id}\""));

        Assert.Equal(EventValidationCodes.EntityIdInvalid, result.Failure?.Code);
    }

    [Theory]
    [InlineData("\"eventId\":\"\"")]
    [InlineData("\"eventId\":\" padded\"")]
    [InlineData("\"eventId\":\"padded \"")]
    [InlineData("\"eventId\":\"bad\\u0000\"")]
    [InlineData("\"eventId\":null")]
    [InlineData("\"eventId\":1")]
    public void InvalidOptionalEventIdsAreRejected(string eventIdProperty)
    {
        var result = EventIngestionTestHelper.ValidateSingle(
            EventIngestionTestHelper.Publish(eventIdProperty: eventIdProperty));

        Assert.Equal(EventValidationCodes.EventIdInvalid, result.Failure?.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void EventIdLengthBoundsAreAcceptedAndExactCaseIsPreserved(int length)
    {
        var eventId = "E" + new string('a', length - 1);

        var validated = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(eventIdProperty: $"\"eventId\":\"{eventId}\""));

        Assert.Equal(eventId, validated.EventId);
    }

    [Fact]
    public void EventIdAboveMaximumLengthIsRejected()
    {
        var eventId = new string('a', 201);

        var result = EventIngestionTestHelper.ValidateSingle(
            EventIngestionTestHelper.Publish(eventIdProperty: $"\"eventId\":\"{eventId}\""));

        Assert.Equal(EventValidationCodes.EventIdInvalid, result.Failure?.Code);
    }

    [Theory]
    [InlineData("2026-07-31T12:34:56Z", "2026-07-31T12:34:56.0000000+00:00")]
    [InlineData("2026-07-31T14:34:56+02:00", "2026-07-31T12:34:56.0000000+00:00")]
    [InlineData("2026-07-31T12:34:56.1234567Z", "2026-07-31T12:34:56.1234567+00:00")]
    public void TimestampRequiresAnOffsetAndNormalizesToUtc(string supplied, string expected)
    {
        var validated = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(timestamp: supplied));

        Assert.Equal(expected, validated.OccurredAtUtc.ToString());
        Assert.Equal(TimeSpan.Zero, validated.OccurredAtUtc.Value.Offset);
    }

    [Theory]
    [InlineData("2026-07-31T12:34:56")]
    [InlineData("2026-07-31T12:34:56.12345678Z")]
    [InlineData("2026-07-31T12:34:56z")]
    [InlineData("2026-07-31 12:34:56Z")]
    [InlineData("not-a-date")]
    public void InvalidTimestampFormatsAreRejected(string timestamp)
    {
        var result = EventIngestionTestHelper.ValidateSingle(
            EventIngestionTestHelper.Publish(timestamp: timestamp));

        Assert.Equal(EventValidationCodes.TimestampInvalid, result.Failure?.Code);
    }

    [Theory]
    [InlineData("1", 1L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    public void PositiveInt64VersionBoundariesAreAccepted(string rawVersion, long expected)
    {
        var validated = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(versionProperty: $"\"version\":{rawVersion}"));

        Assert.Equal(expected, validated.Version?.Value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("9223372036854775808")]
    [InlineData("1.0")]
    [InlineData("1e0")]
    [InlineData("\"1\"")]
    [InlineData("null")]
    public void NonPositiveNonIntegerOrOutOfRangeVersionsAreRejected(string rawVersion)
    {
        var result = EventIngestionTestHelper.ValidateSingle(
            EventIngestionTestHelper.Publish(versionProperty: $"\"version\":{rawVersion}"));

        Assert.Equal(EventValidationCodes.VersionInvalid, result.Failure?.Code);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    public void VersionedEventsRequireObjectPayloads(string rawPayload)
    {
        var result = EventIngestionTestHelper.ValidateSingle(
            EventIngestionTestHelper.Publish(payloadProperty: $"\"payload\":{rawPayload}"));

        Assert.Equal(EventValidationCodes.PayloadMustBeObject, result.Failure?.Code);
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("unpublish")]
    public void AC013VersionedEventsRequireVersionAndPayload(string eventType)
    {
        var missingVersion = $"{{\"type\":\"{eventType}\",\"id\":\"entity-1\",\"timestamp\":\"{EventIngestionTestHelper.Timestamp}\",\"payload\":{{}}}}";
        var missingPayload = $"{{\"type\":\"{eventType}\",\"id\":\"entity-1\",\"version\":1,\"timestamp\":\"{EventIngestionTestHelper.Timestamp}\"}}";

        Assert.Equal(
            EventValidationCodes.VersionRequired,
            EventIngestionTestHelper.ValidateSingle(missingVersion).Failure?.Code);
        Assert.Equal(
            EventValidationCodes.PayloadRequired,
            EventIngestionTestHelper.ValidateSingle(missingPayload).Failure?.Code);
    }

    [Theory]
    [InlineData("\"version\":1", "VERSION_NOT_ALLOWED")]
    [InlineData("\"payload\":{}", "PAYLOAD_NOT_ALLOWED")]
    public void AC013DeleteRejectsVersionAndPayload(string extraProperty, string expectedCode)
    {
        var result = EventIngestionTestHelper.ValidateSingle(
            EventIngestionTestHelper.Delete(extraProperty: extraProperty));

        Assert.Equal(expectedCode, result.Failure?.Code);
    }

    [Fact]
    public void AC054ExactPayloadSizeLimitIsAcceptedAndOneByteMoreIsRejected()
    {
        var acceptedPayload = CreatePayload(CmsEventIngestionLimits.AbsoluteMaximumPayloadSizeBytes);
        var rejectedPayload = CreatePayload(CmsEventIngestionLimits.AbsoluteMaximumPayloadSizeBytes + 1);

        var accepted = EventIngestionTestHelper.ValidateSingle(
            EventIngestionTestHelper.Publish(payloadProperty: $"\"payload\":{acceptedPayload}"));
        var rejected = EventIngestionTestHelper.ValidateSingle(
            EventIngestionTestHelper.Publish(payloadProperty: $"\"payload\":{rejectedPayload}"));

        Assert.True(accepted.IsValid, accepted.Failure?.Code);
        Assert.Equal(EventValidationCodes.PayloadTooLarge, rejected.Failure?.Code);
    }

    [Fact]
    public void PayloadLimitCountsUtf8BytesRatherThanCharacters()
    {
        var limits = new CmsEventIngestionLimits(maximumPayloadSizeBytes: 18);
        const string acceptedPayload = "{\"v\":\"ééééé\"}";
        const string rejectedPayload = "{\"v\":\"éééééé\"}";

        var accepted = EventIngestionTestHelper.ValidateSingle(
            EventIngestionTestHelper.Publish(payloadProperty: $"\"payload\":{acceptedPayload}"),
            limits);
        var rejected = EventIngestionTestHelper.ValidateSingle(
            EventIngestionTestHelper.Publish(payloadProperty: $"\"payload\":{rejectedPayload}"),
            limits);

        Assert.True(accepted.IsValid, accepted.Failure?.Code);
        Assert.Equal(EventValidationCodes.PayloadTooLarge, rejected.Failure?.Code);
    }

    [Fact]
    public void ValidEventsCreateExistingDomainEventTypes()
    {
        var publish = EventIngestionTestHelper.ValidateValid(EventIngestionTestHelper.Publish());
        var unpublish = EventIngestionTestHelper.ValidateValid(EventIngestionTestHelper.Publish("unpublish"));
        var delete = EventIngestionTestHelper.ValidateValid(EventIngestionTestHelper.Delete());

        Assert.IsType<ValidatedPublishEvent>(publish.DomainEvent);
        Assert.IsType<ValidatedUnpublishEvent>(unpublish.DomainEvent);
        Assert.IsType<ValidatedDeleteEvent>(delete.DomainEvent);
        Assert.NotNull(publish.PayloadHash);
        Assert.Null(delete.PayloadHash);
        Assert.Null(delete.Version);
        Assert.Null(delete.RawPayload);
    }

    [Fact]
    public void ResultToStringDoesNotExposeRawPayload()
    {
        const string sentinel = "highly-confidential-payload-sentinel";
        var validated = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(
                payloadProperty: $"\"payload\":{{\"secret\":\"{sentinel}\"}}"));

        Assert.DoesNotContain(sentinel, validated.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidResultAndFailureDiagnosticsDoNotExposeRawPayload()
    {
        const string sentinel = "highly-confidential-payload-sentinel";
        var eventJson = EventIngestionTestHelper.Publish(
            payloadProperty: $"\"payload\":{{\"secret\":\"{sentinel}\",\"secret\":\"duplicate\"}}");

        var item = EventIngestionTestHelper.ParseSingle(eventJson);
        var result = new EventValidator().Validate(item);

        Assert.False(result.IsValid);
        Assert.DoesNotContain(sentinel, item.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, result.Failure?.Message, StringComparison.Ordinal);
    }

    private static string CreatePayload(int desiredUtf8Size)
    {
        const string prefix = "{\"data\":\"";
        const string suffix = "\"}";
        return prefix + new string('a', desiredUtf8Size - prefix.Length - suffix.Length) + suffix;
    }
}
