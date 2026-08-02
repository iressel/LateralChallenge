using CmsSync.Domain.Entities;
using CmsSync.Domain.Events;
using CmsSync.Domain.Processing;
using CmsSync.UnitTests.TestSupport;
using Xunit;

namespace CmsSync.UnitTests.Processing;

public sealed class StateMachineInvariantTests
{
    private const string ConfidentialPayload = "confidential-payload-sentinel";

    [Fact]
    public void ActiveEntityIdMismatchFailsFastWithoutAStateDecision()
    {
        var active = CmsStateTestData.Active(entityId: "other", payload: ConfidentialPayload);

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(CmsStateTestData.VersionedEvent(), active));
    }

    [Fact]
    public void TombstoneEntityIdMismatchFailsFastWithoutAStateDecision()
    {
        var tombstone = CmsStateTestData.Tombstone(entityId: "other");

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                tombstone: tombstone));
    }

    [Fact]
    public void RevisionEntityIdMismatchFailsFastWithoutAStateDecision()
    {
        var active = CmsStateTestData.Active(payload: ConfidentialPayload);
        var revision = CmsStateTestData.Revision(entityId: "other", payload: ConfidentialPayload);

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                active,
                sameVersionRevision: revision));
    }

    [Fact]
    public void RevisionWithoutActiveEntityFailsFastWithoutAStateDecision()
    {
        var revision = CmsStateTestData.Revision(payload: ConfidentialPayload);

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                sameVersionRevision: revision));
    }

    [Fact]
    public void ActiveGenerationAboveOneWithoutTombstoneFailsFastWithoutAStateDecision()
    {
        var active = CmsStateTestData.Active(generation: 2, payload: ConfidentialPayload);

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                active));
    }

    [Fact]
    public void ActiveAndTombstoneGenerationMismatchFailsFastWithoutAStateDecision()
    {
        var active = CmsStateTestData.Active(generation: 3, payload: ConfidentialPayload);
        var tombstone = CmsStateTestData.Tombstone(lastDeletedGeneration: 1);

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                active,
                tombstone));
    }

    [Theory]
    [InlineData(8, 8, 8)]
    [InlineData(9, 9, 10)]
    public void ActiveTimestampsAtOrBeforeRetainedTombstoneFailFast(
        int currentHour,
        int watermarkHour,
        int tombstoneHour)
    {
        var active = CmsStateTestData.Active(
            generation: 2,
            currentVersionOccurredAtUtc: CmsStateTestData.At(currentHour),
            entityEventHighWatermarkUtc: CmsStateTestData.At(watermarkHour),
            payload: ConfidentialPayload);
        var tombstone = CmsStateTestData.Tombstone(
            lastDeletedGeneration: 1,
            deletedAtUtc: CmsStateTestData.At(tombstoneHour));

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(
                    occurredAtUtc: CmsStateTestData.At(11),
                    payload: ConfidentialPayload),
                active,
                tombstone));
    }

    [Fact]
    public void RevisionGenerationMismatchFailsFastWithoutAStateDecision()
    {
        var active = CmsStateTestData.Active(generation: 2, payload: ConfidentialPayload);
        var tombstone = CmsStateTestData.Tombstone(lastDeletedGeneration: 1);
        var revision = CmsStateTestData.Revision(generation: 1, payload: ConfidentialPayload);

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                active,
                tombstone,
                revision));
    }

    [Fact]
    public void RevisionVersionMismatchFailsFastWithoutAStateDecision()
    {
        var active = CmsStateTestData.Active(payload: ConfidentialPayload);
        var revision = CmsStateTestData.Revision(version: 4, payload: ConfidentialPayload);

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                active,
                sameVersionRevision: revision));
    }

    [Fact]
    public void RevisionPayloadHashMismatchFailsFastWithoutAStateDecision()
    {
        var active = CmsStateTestData.Active(payload: ConfidentialPayload);
        var revision = CmsStateTestData.Revision(
            payload: ConfidentialPayload,
            payloadHash: CmsStateTestData.Hash(2));

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                active,
                sameVersionRevision: revision));
    }

    [Fact]
    public void SameVersionWithoutRequiredRevisionFailsFastWithoutAStateDecision()
    {
        var active = CmsStateTestData.Active(payload: ConfidentialPayload);

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                active));
    }

    [Fact]
    public void UnsupportedValidatedEventSubtypeFailsFastWithoutAStateDecision()
    {
        var unsupported = DynamicValidatedEventFactory.Create(
            CmsStateTestData.EntityId,
            CmsStateTestData.At(10));

        AssertSafeInternalFailure(() => CmsEntityStateMachine.Decide(unsupported));
    }

    private static void AssertSafeInternalFailure(Action action)
    {
        var exception = Assert.Throws<InvalidOperationException>(action);

        Assert.DoesNotContain(ConfidentialPayload, exception.Message, StringComparison.Ordinal);
    }

}
