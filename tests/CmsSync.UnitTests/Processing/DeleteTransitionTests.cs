using CmsSync.Domain.Entities;
using CmsSync.Domain.Processing;
using CmsSync.UnitTests.TestSupport;
using Xunit;

namespace CmsSync.UnitTests.Processing;

public sealed class DeleteTransitionTests
{
    [Fact]
    public void AC026ActiveDeleteEarlierThanHighWatermarkIsStaleEvenWhenAfterCurrentVersionTimestamp()
    {
        var active = CmsStateTestData.Active(
            currentVersionOccurredAtUtc: CmsStateTestData.At(9),
            entityEventHighWatermarkUtc: CmsStateTestData.At(10));

        var decision = CmsEntityStateMachine.Decide(
            CmsStateTestData.Delete(CmsStateTestData.At(9, 30)),
            active);

        Assert.Equal(ProcessingOutcome.Stale, decision.Outcome);
        Assert.Equal(ProcessingCodes.DeleteStale, decision.Code);
        Assert.Empty(decision.Operations);
    }

    [Fact]
    public void AC027ActiveDeleteEqualToHighWatermarkConflictsWithoutStateOperations()
    {
        var active = CmsStateTestData.Active(
            currentVersionOccurredAtUtc: CmsStateTestData.At(9),
            entityEventHighWatermarkUtc: CmsStateTestData.At(10));

        var decision = CmsEntityStateMachine.Decide(
            CmsStateTestData.Delete(CmsStateTestData.At(10)),
            active);

        Assert.Equal(ProcessingOutcome.Conflict, decision.Outcome);
        Assert.Equal(ProcessingCodes.DeleteConflict, decision.Code);
        Assert.Empty(decision.Operations);
    }

    [Fact]
    public void AC028ActiveDeleteLaterThanHighWatermarkDeletesEntityAndAllRevisionsAndUpsertsTombstone()
    {
        var active = CmsStateTestData.Active(generation: 3);
        var deleteTimestamp = CmsStateTestData.At(11);

        var decision = CmsEntityStateMachine.Decide(
            CmsStateTestData.Delete(deleteTimestamp),
            active,
            CmsStateTestData.Tombstone(lastDeletedGeneration: 2));

        Assert.Equal(ProcessingOutcome.Applied, decision.Outcome);
        Assert.Equal(ProcessingCodes.EntityDeleted, decision.Code);
        Assert.Collection(
            decision.Operations,
            operation => Assert.Equal(CmsStateTestData.EntityId, Assert.IsType<DeleteAllRevisionsOperation>(operation).EntityId),
            operation => Assert.Equal(CmsStateTestData.EntityId, Assert.IsType<DeleteActiveEntityOperation>(operation).EntityId),
            operation =>
            {
                var tombstone = Assert.IsType<UpsertDeletionTombstoneOperation>(operation).Tombstone;
                Assert.Equal(CmsStateTestData.EntityId, tombstone.EntityId);
                Assert.Equal(3, tombstone.LastDeletedGeneration.Value);
                Assert.Equal(deleteTimestamp, tombstone.DeletedAtUtc);
            });
    }

    [Fact]
    public void AC029FirstDeleteWithoutEntityOrTombstoneCreatesGenerationZeroTombstone()
    {
        var timestamp = CmsStateTestData.At(7);

        var decision = CmsEntityStateMachine.Decide(CmsStateTestData.Delete(timestamp));

        Assert.Equal(ProcessingOutcome.Applied, decision.Outcome);
        Assert.Equal(ProcessingCodes.TombstoneCreated, decision.Code);
        var tombstone = Assert.Single(decision.Operations.OfType<UpsertDeletionTombstoneOperation>()).Tombstone;
        Assert.Equal(0, tombstone.LastDeletedGeneration.Value);
        Assert.Equal(timestamp, tombstone.DeletedAtUtc);
    }

    [Theory]
    [InlineData(-1, ProcessingOutcome.Stale, "TOMBSTONE_STALE", false)]
    [InlineData(0, ProcessingOutcome.Equivalent, "TOMBSTONE_EQUIVALENT", false)]
    [InlineData(1, ProcessingOutcome.Applied, "TOMBSTONE_ADVANCED", true)]
    public void AC029TombstoneOnlyDeleteMatrixPreservesGeneration(
        int timestampDeltaHours,
        ProcessingOutcome expectedOutcome,
        string expectedCode,
        bool expectsOperation)
    {
        var tombstone = CmsStateTestData.Tombstone(
            lastDeletedGeneration: 7,
            deletedAtUtc: CmsStateTestData.At(10));

        var decision = CmsEntityStateMachine.Decide(
            CmsStateTestData.Delete(CmsStateTestData.At(10 + timestampDeltaHours)),
            tombstone: tombstone);

        Assert.Equal(expectedOutcome, decision.Outcome);
        Assert.Equal(expectedCode, decision.Code.Value);

        if (!expectsOperation)
        {
            Assert.Empty(decision.Operations);
            return;
        }

        var updated = Assert.Single(decision.Operations.OfType<UpsertDeletionTombstoneOperation>()).Tombstone;
        Assert.Equal(7, updated.LastDeletedGeneration.Value);
        Assert.Equal(CmsStateTestData.At(11), updated.DeletedAtUtc);
    }

    [Fact]
    public void AC057HigherVersionOlderTimestampKeepsHighWatermarkAndControlsThreeDeleteBoundaries()
    {
        var versionFive = CmsStateTestData.Active(
            version: 5,
            currentVersionOccurredAtUtc: CmsStateTestData.At(10),
            entityEventHighWatermarkUtc: CmsStateTestData.At(10));

        var versionSixDecision = CmsEntityStateMachine.Decide(
            CmsStateTestData.VersionedEvent(version: 6, occurredAtUtc: CmsStateTestData.At(9)),
            versionFive);
        var versionSix = Assert.Single(
            versionSixDecision.Operations.OfType<UpsertActiveEntityOperation>()).Entity;

        Assert.Equal(ProcessingOutcome.Applied, versionSixDecision.Outcome);
        Assert.Equal(6, versionSix.LatestVersion.Value);
        Assert.Equal(CmsStateTestData.At(9), versionSix.CurrentVersionOccurredAtUtc);
        Assert.Equal(CmsStateTestData.At(10), versionSix.EntityEventHighWatermarkUtc);

        var earlierDeleteDecision = CmsEntityStateMachine.Decide(
            CmsStateTestData.Delete(CmsStateTestData.At(9, 30)),
            versionSix);
        var equalDeleteDecision = CmsEntityStateMachine.Decide(
            CmsStateTestData.Delete(CmsStateTestData.At(10)),
            versionSix);
        var laterDeleteDecision = CmsEntityStateMachine.Decide(
            CmsStateTestData.Delete(CmsStateTestData.At(10, 1)),
            versionSix);

        Assert.Equal(ProcessingOutcome.Stale, earlierDeleteDecision.Outcome);
        Assert.Empty(earlierDeleteDecision.Operations);
        Assert.Equal(ProcessingOutcome.Conflict, equalDeleteDecision.Outcome);
        Assert.Empty(equalDeleteDecision.Operations);
        Assert.Equal(ProcessingOutcome.Applied, laterDeleteDecision.Outcome);
        Assert.IsType<DeleteAllRevisionsOperation>(laterDeleteDecision.Operations[0]);
        Assert.IsType<DeleteActiveEntityOperation>(laterDeleteDecision.Operations[1]);
        Assert.IsType<UpsertDeletionTombstoneOperation>(laterDeleteDecision.Operations[2]);
    }
}
