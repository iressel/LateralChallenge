using CmsSync.Domain.Entities;
using CmsSync.Domain.Events;
using CmsSync.Domain.Processing;
using CmsSync.UnitTests.TestSupport;
using Xunit;

namespace CmsSync.UnitTests.Processing;

public sealed class CmsEntityStateMachineTests
{
    public static TheoryData<int, CmsPublicationStatus, CmsPublicationStatus> DifferentPayloadMatrix =>
        new()
        {
            { -1, CmsPublicationStatus.Published, CmsPublicationStatus.Published },
            { -1, CmsPublicationStatus.Published, CmsPublicationStatus.Unpublished },
            { 0, CmsPublicationStatus.Published, CmsPublicationStatus.Published },
            { 0, CmsPublicationStatus.Published, CmsPublicationStatus.Unpublished },
            { 1, CmsPublicationStatus.Published, CmsPublicationStatus.Published },
            { 1, CmsPublicationStatus.Published, CmsPublicationStatus.Unpublished },
        };

    [Theory]
    [InlineData(CmsPublicationStatus.Published, 1L)]
    [InlineData(CmsPublicationStatus.Published, 7L)]
    [InlineData(CmsPublicationStatus.Unpublished, 1L)]
    [InlineData(CmsPublicationStatus.Unpublished, long.MaxValue)]
    public void AC018FirstObservedVersionedEventCreatesGenerationOneAndImmutableRevision(
        CmsPublicationStatus status,
        long version)
    {
        var incoming = CmsStateTestData.VersionedEvent(status, version, CmsStateTestData.At(9));

        var decision = CmsEntityStateMachine.Decide(incoming);

        Assert.Equal(ProcessingOutcome.Applied, decision.Outcome);
        Assert.Equal(ProcessingCodes.EntityCreated, decision.Code);
        var entity = Assert.Single(decision.Operations.OfType<UpsertActiveEntityOperation>()).Entity;
        var revision = Assert.Single(decision.Operations.OfType<InsertRevisionOperation>()).Revision;
        Assert.Equal(1, entity.Generation.Value);
        Assert.Equal(version, entity.LatestVersion.Value);
        Assert.Equal(status, entity.PublicationStatus);
        Assert.Equal(CmsStateTestData.At(9), entity.CurrentVersionOccurredAtUtc);
        Assert.Equal(CmsStateTestData.At(9), entity.EntityEventHighWatermarkUtc);
        Assert.False(entity.AdministrativeDisabled);
        Assert.Equal(entity.EntityId, revision.EntityId);
        Assert.Equal(entity.Generation, revision.Generation);
        Assert.Equal(entity.LatestVersion, revision.Version);
        Assert.Equal(entity.Payload, revision.FirstObservedPayload);
        Assert.Equal(entity.PayloadHash, revision.PayloadHash);
    }

    [Fact]
    public void AC014ArbitraryFirstVersionAndHigherVersionGapAreAccepted()
    {
        var first = CmsEntityStateMachine.Decide(
            CmsStateTestData.VersionedEvent(version: 7, occurredAtUtc: CmsStateTestData.At(10)));
        var active = Assert.Single(first.Operations.OfType<UpsertActiveEntityOperation>()).Entity;

        var later = CmsEntityStateMachine.Decide(
            CmsStateTestData.VersionedEvent(
                version: 10,
                occurredAtUtc: CmsStateTestData.At(11),
                payload: CmsStateTestData.DifferentPayload,
                payloadHash: CmsStateTestData.Hash(2)),
            active);

        Assert.Equal(ProcessingOutcome.Applied, later.Outcome);
        Assert.Equal(ProcessingCodes.VersionAdvanced, later.Code);
        Assert.Equal(10, Assert.Single(later.Operations.OfType<UpsertActiveEntityOperation>()).Entity.LatestVersion.Value);
    }

    [Fact]
    public void AC019LowerVersionIsStaleAndProducesNoStateOperations()
    {
        var active = CmsStateTestData.Active(version: 5);

        var decision = CmsEntityStateMachine.Decide(
            CmsStateTestData.VersionedEvent(
                version: 4,
                occurredAtUtc: CmsStateTestData.At(12),
                payload: CmsStateTestData.DifferentPayload,
                payloadHash: CmsStateTestData.Hash(2)),
            active);

        Assert.Equal(ProcessingOutcome.Stale, decision.Outcome);
        Assert.Equal(ProcessingCodes.VersionStale, decision.Code);
        Assert.Empty(decision.Operations);
    }

    [Theory]
    [InlineData(CmsPublicationStatus.Published)]
    [InlineData(CmsPublicationStatus.Unpublished)]
    public void HigherVersionStoresIncomingPayloadAndStatusWhilePreservingLocalState(
        CmsPublicationStatus incomingStatus)
    {
        var active = CmsStateTestData.Active(administrativeDisabled: true);
        var incomingHash = CmsStateTestData.Hash(2);
        var incomingTimestamp = CmsStateTestData.At(9);
        var incoming = CmsStateTestData.VersionedEvent(
            incomingStatus,
            version: 8,
            occurredAtUtc: incomingTimestamp,
            payload: CmsStateTestData.DifferentPayload,
            payloadHash: incomingHash);

        var decision = CmsEntityStateMachine.Decide(incoming, active);

        Assert.Equal(ProcessingOutcome.Applied, decision.Outcome);
        var entity = Assert.Single(decision.Operations.OfType<UpsertActiveEntityOperation>()).Entity;
        var revision = Assert.Single(decision.Operations.OfType<InsertRevisionOperation>()).Revision;
        Assert.Equal(8, entity.LatestVersion.Value);
        Assert.Equal(CmsStateTestData.DifferentPayload, entity.Payload);
        Assert.Equal(incomingHash, entity.PayloadHash);
        Assert.Equal(incomingStatus, entity.PublicationStatus);
        Assert.Equal(incomingTimestamp, entity.CurrentVersionOccurredAtUtc);
        Assert.Equal(CmsStateTestData.At(10), entity.EntityEventHighWatermarkUtc);
        Assert.True(entity.AdministrativeDisabled);
        Assert.Equal(CmsStateTestData.DifferentPayload, revision.FirstObservedPayload);
        Assert.Equal(incomingHash, revision.PayloadHash);
    }

    [Fact]
    public void AC020UnpublishAtXPlusOneStoresItsRevisionAndPreservesAdministrativeDisable()
    {
        var active = CmsStateTestData.Active(
            version: 5,
            status: CmsPublicationStatus.Published,
            administrativeDisabled: true);
        var incomingHash = CmsStateTestData.Hash(2);

        var decision = CmsEntityStateMachine.Decide(
            CmsStateTestData.VersionedEvent(
                CmsPublicationStatus.Unpublished,
                version: 6,
                occurredAtUtc: CmsStateTestData.At(11),
                payload: CmsStateTestData.DifferentPayload,
                payloadHash: incomingHash),
            active);

        Assert.Equal(ProcessingOutcome.Applied, decision.Outcome);
        var entity = Assert.Single(decision.Operations.OfType<UpsertActiveEntityOperation>()).Entity;
        var revision = Assert.Single(decision.Operations.OfType<InsertRevisionOperation>()).Revision;
        Assert.Equal(6, entity.LatestVersion.Value);
        Assert.Equal(CmsPublicationStatus.Unpublished, entity.PublicationStatus);
        Assert.Equal(CmsStateTestData.DifferentPayload, entity.Payload);
        Assert.Equal(incomingHash, entity.PayloadHash);
        Assert.Equal(CmsStateTestData.At(11), entity.CurrentVersionOccurredAtUtc);
        Assert.Equal(CmsStateTestData.At(11), entity.EntityEventHighWatermarkUtc);
        Assert.True(entity.AdministrativeDisabled);
        Assert.Equal(new EntityVersion(6), revision.Version);
        Assert.Equal(incomingHash, revision.PayloadHash);
    }

    [Theory]
    [InlineData(CmsPublicationStatus.Published)]
    [InlineData(CmsPublicationStatus.Unpublished)]
    public void AC022SameVersionEarlierTimestampIsStaleRegardlessOfStatus(
        CmsPublicationStatus incomingStatus)
    {
        var active = CmsStateTestData.Active(status: CmsPublicationStatus.Published);

        var decision = CmsEntityStateMachine.Decide(
            CmsStateTestData.VersionedEvent(incomingStatus, occurredAtUtc: CmsStateTestData.At(9)),
            active,
            sameVersionRevision: CmsStateTestData.Revision());

        Assert.Equal(ProcessingOutcome.Stale, decision.Outcome);
        Assert.Equal(ProcessingCodes.EventTimestampStale, decision.Code);
        Assert.Empty(decision.Operations);
    }

    [Theory]
    [InlineData(CmsPublicationStatus.Published)]
    [InlineData(CmsPublicationStatus.Unpublished)]
    public void AC023SameVersionEqualTimestampAndSameStatusIsEquivalent(
        CmsPublicationStatus status)
    {
        var active = CmsStateTestData.Active(status: status);

        var decision = CmsEntityStateMachine.Decide(
            CmsStateTestData.VersionedEvent(status),
            active,
            sameVersionRevision: CmsStateTestData.Revision());

        Assert.Equal(ProcessingOutcome.Equivalent, decision.Outcome);
        Assert.Equal(ProcessingCodes.StateEquivalent, decision.Code);
        Assert.Empty(decision.Operations);
    }

    [Theory]
    [InlineData(CmsPublicationStatus.Published, CmsPublicationStatus.Unpublished)]
    [InlineData(CmsPublicationStatus.Unpublished, CmsPublicationStatus.Published)]
    public void AC024SameVersionEqualTimestampAndDifferentStatusConflicts(
        CmsPublicationStatus currentStatus,
        CmsPublicationStatus incomingStatus)
    {
        var active = CmsStateTestData.Active(status: currentStatus);

        var decision = CmsEntityStateMachine.Decide(
            CmsStateTestData.VersionedEvent(incomingStatus),
            active,
            sameVersionRevision: CmsStateTestData.Revision());

        Assert.Equal(ProcessingOutcome.Conflict, decision.Outcome);
        Assert.Equal(ProcessingCodes.PublicationStatusConflict, decision.Code);
        Assert.Empty(decision.Operations);
    }

    [Theory]
    [InlineData(CmsPublicationStatus.Published, CmsPublicationStatus.Published)]
    [InlineData(CmsPublicationStatus.Published, CmsPublicationStatus.Unpublished)]
    [InlineData(CmsPublicationStatus.Unpublished, CmsPublicationStatus.Unpublished)]
    [InlineData(CmsPublicationStatus.Unpublished, CmsPublicationStatus.Published)]
    public void AC025SameVersionLaterTimestampAppliesWithoutCreatingARevision(
        CmsPublicationStatus currentStatus,
        CmsPublicationStatus incomingStatus)
    {
        var active = CmsStateTestData.Active(
            status: currentStatus,
            entityEventHighWatermarkUtc: CmsStateTestData.At(12),
            administrativeDisabled: true);

        var decision = CmsEntityStateMachine.Decide(
            CmsStateTestData.VersionedEvent(incomingStatus, occurredAtUtc: CmsStateTestData.At(11)),
            active,
            sameVersionRevision: CmsStateTestData.Revision());

        Assert.Equal(ProcessingOutcome.Applied, decision.Outcome);
        Assert.Equal(ProcessingCodes.SameVersionApplied, decision.Code);
        var entity = Assert.Single(decision.Operations.OfType<UpsertActiveEntityOperation>()).Entity;
        Assert.Empty(decision.Operations.OfType<InsertRevisionOperation>());
        Assert.Equal(CmsStateTestData.At(11), entity.CurrentVersionOccurredAtUtc);
        Assert.Equal(CmsStateTestData.At(12), entity.EntityEventHighWatermarkUtc);
        Assert.Equal(incomingStatus, entity.PublicationStatus);
        Assert.True(entity.AdministrativeDisabled);
        Assert.Equal(active.Payload, entity.Payload);
        Assert.Equal(active.PayloadHash, entity.PayloadHash);
    }

    [Theory]
    [MemberData(nameof(DifferentPayloadMatrix))]
    public void AC021DifferentSameVersionPayloadAlwaysConflictsBeforeTimestampOrStatus(
        int timestampDeltaHours,
        CmsPublicationStatus currentStatus,
        CmsPublicationStatus incomingStatus)
    {
        var active = CmsStateTestData.Active(status: currentStatus);
        var revision = CmsStateTestData.Revision();
        var originalPayload = revision.FirstObservedPayload;
        var originalHash = revision.PayloadHash;

        var decision = CmsEntityStateMachine.Decide(
            CmsStateTestData.VersionedEvent(
                incomingStatus,
                occurredAtUtc: CmsStateTestData.At(10 + timestampDeltaHours),
                payload: CmsStateTestData.DifferentPayload,
                payloadHash: CmsStateTestData.Hash(2)),
            active,
            sameVersionRevision: revision);

        Assert.Equal(ProcessingOutcome.Conflict, decision.Outcome);
        Assert.Equal(ProcessingCodes.PayloadConflict, decision.Code);
        Assert.Empty(decision.Operations);
        Assert.Equal(originalPayload, revision.FirstObservedPayload);
        Assert.Same(originalHash, revision.PayloadHash);
        Assert.Equal(CmsStateTestData.Payload, active.Payload);
        Assert.Equal(CmsStateTestData.Hash(), active.PayloadHash);
    }

    [Fact]
    public void AC033AllAppliedCmsTransitionsPreserveAdministrativeDisabled()
    {
        var active = CmsStateTestData.Active(administrativeDisabled: true);
        var revision = CmsStateTestData.Revision();

        var higherVersion = CmsEntityStateMachine.Decide(
            CmsStateTestData.VersionedEvent(version: 6, occurredAtUtc: CmsStateTestData.At(11)),
            active);
        var sameVersionLater = CmsEntityStateMachine.Decide(
            CmsStateTestData.VersionedEvent(
                CmsPublicationStatus.Unpublished,
                occurredAtUtc: CmsStateTestData.At(11)),
            active,
            sameVersionRevision: revision);

        Assert.True(Assert.Single(higherVersion.Operations.OfType<UpsertActiveEntityOperation>()).Entity.AdministrativeDisabled);
        Assert.True(Assert.Single(sameVersionLater.Operations.OfType<UpsertActiveEntityOperation>()).Entity.AdministrativeDisabled);
    }

    [Fact]
    public void DecideDoesNotMutateInputSnapshotsEventsOrHashBuffers()
    {
        var activeHashBytes = Enumerable.Repeat((byte)1, PayloadHash.Length).ToArray();
        var activeHash = new PayloadHash(activeHashBytes);
        var active = CmsStateTestData.Active(payloadHash: activeHash, administrativeDisabled: true);
        var eventHashBytes = Enumerable.Repeat((byte)2, PayloadHash.Length).ToArray();
        var eventHash = new PayloadHash(eventHashBytes);
        var incoming = CmsStateTestData.VersionedEvent(
            version: 6,
            occurredAtUtc: CmsStateTestData.At(9),
            payload: CmsStateTestData.DifferentPayload,
            payloadHash: eventHash);

        _ = CmsEntityStateMachine.Decide(incoming, active);
        activeHashBytes[0] = 9;
        eventHashBytes[0] = 9;

        Assert.Equal(CmsStateTestData.Payload, active.Payload);
        Assert.Equal(CmsStateTestData.Hash(), active.PayloadHash);
        Assert.Equal(CmsStateTestData.DifferentPayload, ((ValidatedPublishEvent)incoming).Payload);
        Assert.Equal(CmsStateTestData.Hash(2), ((ValidatedPublishEvent)incoming).PayloadHash);
    }

    [Fact]
    public void AC053PureDecisionIsDeterministicAndNeverRegressesTheHighWatermark()
    {
        var active = CmsStateTestData.Active(
            currentVersionOccurredAtUtc: CmsStateTestData.At(10),
            entityEventHighWatermarkUtc: CmsStateTestData.At(12));
        var incoming = CmsStateTestData.VersionedEvent(version: 6, occurredAtUtc: CmsStateTestData.At(9));

        var first = CmsEntityStateMachine.Decide(incoming, active);
        var second = CmsEntityStateMachine.Decide(incoming, active);

        Assert.Equal(first.Outcome, second.Outcome);
        Assert.Equal(first.Code, second.Code);
        Assert.Equal(first.Operations, second.Operations);
        var resultingEntity = Assert.Single(first.Operations.OfType<UpsertActiveEntityOperation>()).Entity;
        Assert.Equal(CmsStateTestData.At(9), resultingEntity.CurrentVersionOccurredAtUtc);
        Assert.Equal(CmsStateTestData.At(12), resultingEntity.EntityEventHighWatermarkUtc);
    }
}
