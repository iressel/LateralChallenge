using CmsSync.Application.EventIngestion;
using CmsSync.Domain.Entities;
using CmsSync.Domain.Events;
using CmsSync.UnitTests.TestSupport;
using Xunit;

namespace CmsSync.UnitTests.Processing;

public sealed class ValueObjectTests
{
    [Theory]
    [InlineData(1L)]
    [InlineData(long.MaxValue)]
    public void EntityVersionAcceptsPositiveInt64Boundaries(long value)
    {
        var version = new EntityVersion(value);

        Assert.True(version.IsValid);
        Assert.Equal(value, version.Value);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void EntityVersionRejectsZeroAndNegativeValues(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntityVersion(value));
    }

    [Fact]
    public void EntityGenerationZeroIsAValidTombstoneValueButNotAnActiveGeneration()
    {
        var generation = new EntityGeneration(0);
        var tombstone = new CmsDeletionTombstoneSnapshot(
            CmsStateTestData.EntityId,
            generation,
            CmsStateTestData.At(10));

        Assert.False(generation.IsActive);
        Assert.Equal(0, tombstone.LastDeletedGeneration.Value);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CmsStateTestData.Active(generation: 0));
    }

    [Fact]
    public void EntityGenerationAtInt64MaximumCannotOverflow()
    {
        var generation = new EntityGeneration(long.MaxValue);

        var hasNext = generation.TryGetNext(out var next);

        Assert.False(hasNext);
        Assert.Equal(default, next);
    }

    [Theory]
    [InlineData(PayloadHash.Length - 1)]
    [InlineData(PayloadHash.Length + 1)]
    public void PayloadHashRequiresExactlyThirtyTwoBytes(int length)
    {
        Assert.Throws<ArgumentException>(() => new PayloadHash(new byte[length]));
    }

    [Fact]
    public void PayloadHashEqualityIsContentBasedAndCopiesInputAndOutputBuffers()
    {
        var source = Enumerable.Repeat((byte)7, PayloadHash.Length).ToArray();
        var first = new PayloadHash(source);
        var second = new PayloadHash(source);

        source[0] = 9;
        var returned = first.ToArray();
        returned[1] = 9;

        Assert.Equal(first, second);
        Assert.Equal(7, first.ToArray()[0]);
        Assert.Equal(7, first.ToArray()[1]);
        Assert.Matches("^[0-9A-F]{64}$", first.ToString());
    }

    [Theory]
    [InlineData(EventContentHash.Length - 1)]
    [InlineData(EventContentHash.Length + 1)]
    public void EventContentHashRequiresExactlyThirtyTwoBytes(int length)
    {
        Assert.Throws<ArgumentException>(() => new EventContentHash(new byte[length]));
    }

    [Fact]
    public void EventContentHashEqualityIsContentBasedAndCopiesInputAndOutputBuffers()
    {
        var source = Enumerable.Repeat((byte)8, EventContentHash.Length).ToArray();
        var first = new EventContentHash(source);
        var second = new EventContentHash(source);

        source[0] = 9;
        var returned = first.ToArray();
        returned[1] = 9;

        Assert.Equal(first, second);
        Assert.Equal(8, first.ToArray()[0]);
        Assert.Equal(8, first.ToArray()[1]);
        Assert.Matches("^[0-9A-F]{64}$", first.ToString());
    }

    [Fact]
    public void UtcTimestampRejectsNonZeroOffsets()
    {
        var value = new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.FromHours(2));

        Assert.Throws<ArgumentException>(() => new UtcTimestamp(value));
    }

    [Fact]
    public void UtcTimestampMaxReturnsTheLaterTimestampIncludingEqualityBoundary()
    {
        var earlier = CmsStateTestData.At(9);
        var later = CmsStateTestData.At(10);

        Assert.Equal(later, UtcTimestamp.Max(earlier, later));
        Assert.Equal(later, UtcTimestamp.Max(later, earlier));
        Assert.Equal(later, UtcTimestamp.Max(later, later));
    }

    [Fact]
    public void DefaultVersionAndGenerationCannotEnterValidActiveSnapshotsOrVersionedEvents()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ActiveCmsEntitySnapshot(
                CmsStateTestData.EntityId,
                default,
                new EntityVersion(1),
                CmsStateTestData.Payload,
                CmsStateTestData.Hash(),
                CmsPublicationStatus.Published,
                CmsStateTestData.At(10),
                CmsStateTestData.At(10),
                administrativeDisabled: false));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ValidatedPublishEvent(
                CmsStateTestData.EntityId,
                default,
                CmsStateTestData.At(10),
                CmsStateTestData.Payload,
                CmsStateTestData.Hash()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ValidatedUnpublishEvent(
                CmsStateTestData.EntityId,
                default,
                CmsStateTestData.At(10),
                CmsStateTestData.Payload,
                CmsStateTestData.Hash()));
    }

    [Fact]
    public void InvalidEnumNullHashesAndRegressiveTimestampsCannotEnterValidSnapshotsOrEvents()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CmsStateTestData.Active(status: (CmsPublicationStatus)999));
        Assert.Throws<ArgumentNullException>(
            () => new ValidatedPublishEvent(
                CmsStateTestData.EntityId,
                new EntityVersion(1),
                CmsStateTestData.At(10),
                CmsStateTestData.Payload,
                payloadHash: null!));
        Assert.Throws<ArgumentException>(
            () => CmsStateTestData.Active(
                currentVersionOccurredAtUtc: CmsStateTestData.At(10),
                entityEventHighWatermarkUtc: CmsStateTestData.At(9)));
    }

    [Fact]
    public void PayloadAndEventHashDiagnosticsContainOnlyStableHex()
    {
        const string confidentialPayload = "confidential-payload-sentinel";
        var payloadHash = CmsStateTestData.Hash();
        var eventHash = new EventContentHash(Enumerable.Repeat((byte)2, EventContentHash.Length).ToArray());

        Assert.DoesNotContain(confidentialPayload, payloadHash.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(confidentialPayload, eventHash.ToString(), StringComparison.Ordinal);
        Assert.Matches("^[0-9A-F]{64}$", payloadHash.ToString());
        Assert.Matches("^[0-9A-F]{64}$", eventHash.ToString());
    }
}
