using CmsSync.Application.EventIngestion;
using Xunit;

namespace CmsSync.UnitTests.EventIngestion;

public sealed class EventIdentityFactoryTests
{
    public static TheoryData<string, string, string, string> GoldenIdentityVectors =>
        new()
        {
            {
                "publish",
                EventIngestionTestHelper.Publish(),
                "sha256:3708377F00ABA72C022B35434E1AF80546F3455300F48DEF872A7D4BC03C23EE",
                "3708377F00ABA72C022B35434E1AF80546F3455300F48DEF872A7D4BC03C23EE"
            },
            {
                "unpublish",
                EventIngestionTestHelper.Publish(type: "unpublish"),
                "sha256:09F651C1BEBAF43197AD502FCEAB3977D01D6A10C12F50C6E11C313468719607",
                "09F651C1BEBAF43197AD502FCEAB3977D01D6A10C12F50C6E11C313468719607"
            },
            {
                "delete",
                EventIngestionTestHelper.Delete(),
                "sha256:43134E735877F7C1B57050041FDDE738D55B1738A87206D4A04C08F50B7DA572",
                "43134E735877F7C1B57050041FDDE738D55B1738A87206D4A04C08F50B7DA572"
            },
            {
                "external EventId",
                EventIngestionTestHelper.Publish(
                    eventIdProperty: "\"eventId\":\"External-Golden\"",
                    idProperty: "\"id\":\"entity-external\"",
                    versionProperty: "\"version\":1",
                    payloadProperty: "\"payload\":{}"),
                "external:External-Golden",
                "4E59D0044C3F32CAA1A219629477D4E048296BC19CEDF209C4C5B00672E36482"
            },
        };

    [Theory]
    [MemberData(nameof(GoldenIdentityVectors))]
    public void GoldenIdentityHasReviewedStableKeyAndUppercaseContentHash(
        string vectorName,
        string eventJson,
        string expectedKey,
        string expectedContentHash)
    {
        var first = EventIngestionTestHelper.ValidateValid(eventJson);
        var replay = EventIngestionTestHelper.ValidateValid(eventJson);

        Assert.False(string.IsNullOrWhiteSpace(vectorName));
        Assert.Equal(expectedKey, first.IdempotencyKey);
        Assert.Equal(expectedContentHash, first.EventContentHash.ToString());
        Assert.Matches("^[0-9A-F]{64}$", first.EventContentHash.ToString());
        Assert.Equal(first.IdempotencyKey, replay.IdempotencyKey);
        Assert.Equal(first.EventContentHash, replay.EventContentHash);
    }

    [Fact]
    public void AC017ExactReplayWithoutEventIdUsesTheSameUppercaseDerivedKey()
    {
        var first = EventIngestionTestHelper.ValidateValid(EventIngestionTestHelper.Publish());
        var replay = EventIngestionTestHelper.ValidateValid(EventIngestionTestHelper.Publish());

        Assert.Equal(first.IdempotencyKey, replay.IdempotencyKey);
        Assert.Equal(first.EventContentHash, replay.EventContentHash);
        Assert.StartsWith("sha256:", first.IdempotencyKey, StringComparison.Ordinal);
        Assert.Matches("^sha256:[0-9A-F]{64}$", first.IdempotencyKey);
    }

    [Fact]
    public void AC015ExactReplayWithEventIdUsesTheSameExternalKeyAndContentHash()
    {
        var first = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(eventIdProperty: "\"eventId\":\"Event-AbC\""));
        var replay = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(eventIdProperty: "\"eventId\":\"Event-AbC\""));

        Assert.Equal("external:Event-AbC", first.IdempotencyKey);
        Assert.Equal("Event-AbC", first.EventId);
        Assert.Equal(first.IdempotencyKey, replay.IdempotencyKey);
        Assert.Equal(first.EventContentHash, replay.EventContentHash);
    }

    [Fact]
    public void EventIdIsExcludedFromEventContentHash()
    {
        var first = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(eventIdProperty: "\"eventId\":\"first\""));
        var second = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(eventIdProperty: "\"eventId\":\"second\""));

        Assert.NotEqual(first.IdempotencyKey, second.IdempotencyKey);
        Assert.Equal(first.EventContentHash, second.EventContentHash);
    }

    [Fact]
    public void AC016SameEventIdWithDifferentKnownContentHasDifferentContentHashes()
    {
        var first = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(eventIdProperty: "\"eventId\":\"same\""));
        var second = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(
                eventIdProperty: "\"eventId\":\"same\"",
                idProperty: "\"id\":\"entity-2\""));

        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
        Assert.NotEqual(first.EventContentHash, second.EventContentHash);
    }

    [Fact]
    public void EntityIdCaseIsSignificantForNormalizedContent()
    {
        var upper = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(idProperty: "\"id\":\"Entity\""));
        var lower = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(idProperty: "\"id\":\"entity\""));

        Assert.NotEqual(upper.EventContentHash, lower.EventContentHash);
        Assert.NotEqual(upper.IdempotencyKey, lower.IdempotencyKey);
    }

    [Fact]
    public void UnknownEnvelopeFieldsDoNotAffectNormalizedContent()
    {
        var first = EventIngestionTestHelper.ValidateValid(EventIngestionTestHelper.Publish());
        var second = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(unknownProperty: "\"ignored\":[1,2,3]"));

        Assert.Equal(first.EventContentHash, second.EventContentHash);
        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
    }

    [Fact]
    public void TimestampChangesAffectNormalizedContent()
    {
        var first = EventIngestionTestHelper.ValidateValid(EventIngestionTestHelper.Publish());
        var second = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(timestamp: "2026-07-31T12:34:56.1234566Z"));

        Assert.NotEqual(first.EventContentHash, second.EventContentHash);
        Assert.NotEqual(first.IdempotencyKey, second.IdempotencyKey);
    }

    [Fact]
    public void EquivalentUtcOffsetsProduceTheSameNormalizedContent()
    {
        var first = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(timestamp: "2026-07-31T12:34:56Z"));
        var second = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(timestamp: "2026-07-31T14:34:56+02:00"));

        Assert.Equal(first.EventContentHash, second.EventContentHash);
        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
    }

    [Theory]
    [InlineData("publish", "Publish")]
    [InlineData("publish", "  PUBLISH ")]
    [InlineData("unpublish", "unPublish")]
    [InlineData("unpublish", " UNPUBLISH\t")]
    [InlineData("delete", "DELETE")]
    [InlineData("delete", " Delete ")]
    public void AcceptedTypeVariantsHaveTheSameCanonicalHashAndDerivedKey(
        string canonical,
        string variant)
    {
        var firstJson = canonical == "delete"
            ? EventIngestionTestHelper.Delete(canonical)
            : EventIngestionTestHelper.Publish(canonical);
        var secondJson = canonical == "delete"
            ? EventIngestionTestHelper.Delete(variant)
            : EventIngestionTestHelper.Publish(variant);

        var first = EventIngestionTestHelper.ValidateValid(firstJson);
        var second = EventIngestionTestHelper.ValidateValid(secondJson);

        Assert.Equal(first.CanonicalEventType, second.CanonicalEventType);
        Assert.Equal(first.EventContentHash, second.EventContentHash);
        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
    }

    [Fact]
    public void CanonicallyEquivalentPayloadsHaveSameHashesButPreserveDifferentRawText()
    {
        var first = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(payloadProperty: "\"payload\":{\"a\":1,\"b\":2}"));
        var second = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(payloadProperty: "\"payload\":{ \"b\" : 2, \"a\" : 1 }"));

        Assert.NotEqual(first.RawPayload, second.RawPayload);
        Assert.Equal(first.PayloadHash, second.PayloadHash);
        Assert.Equal(first.EventContentHash, second.EventContentHash);
        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
    }

    [Fact]
    public void NumericTokenSpellingChangesPayloadAndEventHashes()
    {
        var integer = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(payloadProperty: "\"payload\":{\"number\":1}"));
        var floatingPoint = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(payloadProperty: "\"payload\":{\"number\":1.0}"));

        Assert.NotEqual(integer.PayloadHash, floatingPoint.PayloadHash);
        Assert.NotEqual(integer.EventContentHash, floatingPoint.EventContentHash);
        Assert.NotEqual(integer.IdempotencyKey, floatingPoint.IdempotencyKey);
    }

    [Fact]
    public void DeleteIdentityUsesExplicitUnversionedAndNoPayloadSentinels()
    {
        var delete = EventIngestionTestHelper.ValidateValid(EventIngestionTestHelper.Delete());
        var publish = EventIngestionTestHelper.ValidateValid(EventIngestionTestHelper.Publish());

        Assert.Null(delete.Version);
        Assert.Null(delete.PayloadHash);
        Assert.NotEqual(delete.EventContentHash, publish.EventContentHash);
        Assert.NotEqual(delete.IdempotencyKey, publish.IdempotencyKey);
    }

    [Fact]
    public void ExternalAndDerivedNamespacesCannotCollide()
    {
        var derived = EventIngestionTestHelper.ValidateValid(EventIngestionTestHelper.Publish());
        var external = EventIngestionTestHelper.ValidateValid(
            EventIngestionTestHelper.Publish(
                eventIdProperty: $"\"eventId\":\"{derived.IdempotencyKey}\""));

        Assert.StartsWith("sha256:", derived.IdempotencyKey, StringComparison.Ordinal);
        Assert.StartsWith("external:", external.IdempotencyKey, StringComparison.Ordinal);
        Assert.NotEqual(derived.IdempotencyKey, external.IdempotencyKey);
    }
}
