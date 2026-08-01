using CmsSync.Domain.Entities;
using CmsSync.Domain.Processing;
using CmsSync.UnitTests.TestSupport;
using Xunit;

namespace CmsSync.UnitTests.Processing;

public sealed class RecreationTests
{
    [Theory]
    [InlineData(CmsPublicationStatus.Published, -1)]
    [InlineData(CmsPublicationStatus.Published, 0)]
    [InlineData(CmsPublicationStatus.Unpublished, -1)]
    [InlineData(CmsPublicationStatus.Unpublished, 0)]
    public void AC030PublishAndUnpublishAtOrBeforeTombstoneAreStaleRegardlessOfVersion(
        CmsPublicationStatus status,
        int timestampDeltaHours)
    {
        var tombstone = CmsStateTestData.Tombstone(
            lastDeletedGeneration: 4,
            deletedAtUtc: CmsStateTestData.At(10));

        var decision = CmsEntityStateMachine.Decide(
            CmsStateTestData.VersionedEvent(
                status,
                version: long.MaxValue,
                occurredAtUtc: CmsStateTestData.At(10 + timestampDeltaHours)),
            tombstone: tombstone);

        Assert.Equal(ProcessingOutcome.Stale, decision.Outcome);
        Assert.Equal(ProcessingCodes.TombstoneBlocked, decision.Code);
        Assert.Empty(decision.Operations);
    }

    [Theory]
    [InlineData(CmsPublicationStatus.Published, 1L)]
    [InlineData(CmsPublicationStatus.Published, 7L)]
    [InlineData(CmsPublicationStatus.Published, long.MaxValue)]
    [InlineData(CmsPublicationStatus.Unpublished, 1L)]
    [InlineData(CmsPublicationStatus.Unpublished, 7L)]
    [InlineData(CmsPublicationStatus.Unpublished, long.MaxValue)]
    public void AC031AndAC032LaterPublishOrUnpublishRecreatesNextGenerationWithAnyPositiveVersion(
        CmsPublicationStatus status,
        long version)
    {
        var tombstone = CmsStateTestData.Tombstone(
            lastDeletedGeneration: 4,
            deletedAtUtc: CmsStateTestData.At(10));
        var timestamp = CmsStateTestData.At(11);

        var decision = CmsEntityStateMachine.Decide(
            CmsStateTestData.VersionedEvent(status, version, timestamp),
            tombstone: tombstone);

        Assert.Equal(ProcessingOutcome.Applied, decision.Outcome);
        Assert.Equal(ProcessingCodes.EntityRecreated, decision.Code);
        Assert.Collection(
            decision.Operations,
            operation =>
            {
                var entity = Assert.IsType<UpsertActiveEntityOperation>(operation).Entity;
                Assert.Equal(5, entity.Generation.Value);
                Assert.Equal(version, entity.LatestVersion.Value);
                Assert.Equal(status, entity.PublicationStatus);
                Assert.Equal(timestamp, entity.CurrentVersionOccurredAtUtc);
                Assert.Equal(timestamp, entity.EntityEventHighWatermarkUtc);
                Assert.False(entity.AdministrativeDisabled);
            },
            operation =>
            {
                var revision = Assert.IsType<InsertRevisionOperation>(operation).Revision;
                Assert.Equal(5, revision.Generation.Value);
                Assert.Equal(version, revision.Version.Value);
                Assert.Equal(timestamp, revision.FirstObservedAtUtc);
            });
        Assert.DoesNotContain(
            decision.Operations,
            operation => operation is UpsertDeletionTombstoneOperation or DeleteActiveEntityOperation);
        Assert.Equal(4, tombstone.LastDeletedGeneration.Value);
        Assert.Equal(CmsStateTestData.At(10), tombstone.DeletedAtUtc);
    }

    [Fact]
    public void RecreationAtMaximumGenerationReturnsDeterministicGenerationExhaustedConflict()
    {
        var tombstone = CmsStateTestData.Tombstone(
            lastDeletedGeneration: long.MaxValue,
            deletedAtUtc: CmsStateTestData.At(10));

        var exception = Record.Exception(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(occurredAtUtc: CmsStateTestData.At(11)),
                tombstone: tombstone));

        Assert.Null(exception);
        var decision = CmsEntityStateMachine.Decide(
            CmsStateTestData.VersionedEvent(occurredAtUtc: CmsStateTestData.At(11)),
            tombstone: tombstone);
        Assert.Equal(ProcessingOutcome.Conflict, decision.Outcome);
        Assert.Equal(ProcessingCodes.GenerationExhausted, decision.Code);
        Assert.Empty(decision.Operations);
    }

    [Fact]
    public void AC034DeleteRemovesTheOldAdministrativeOverrideAndRecreationStartsEnabled()
    {
        var disabledEntity = CmsStateTestData.Active(administrativeDisabled: true);
        var deleteDecision = CmsEntityStateMachine.Decide(
            CmsStateTestData.Delete(CmsStateTestData.At(11)),
            disabledEntity);
        var tombstone = Assert.Single(
            deleteDecision.Operations.OfType<UpsertDeletionTombstoneOperation>()).Tombstone;

        var recreationDecision = CmsEntityStateMachine.Decide(
            CmsStateTestData.VersionedEvent(
                CmsPublicationStatus.Unpublished,
                version: 9,
                occurredAtUtc: CmsStateTestData.At(12)),
            tombstone: tombstone);
        var recreated = Assert.Single(
            recreationDecision.Operations.OfType<UpsertActiveEntityOperation>()).Entity;

        Assert.Equal(ProcessingOutcome.Applied, deleteDecision.Outcome);
        Assert.Equal(ProcessingOutcome.Applied, recreationDecision.Outcome);
        Assert.Equal(2, recreated.Generation.Value);
        Assert.False(recreated.AdministrativeDisabled);
    }
}
