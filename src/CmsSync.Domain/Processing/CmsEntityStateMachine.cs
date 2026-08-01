using CmsSync.Domain.Entities;
using CmsSync.Domain.Events;

namespace CmsSync.Domain.Processing;

public static class CmsEntityStateMachine
{
    public static ProcessingDecision Decide(
        ValidatedCmsEvent incomingEvent,
        ActiveCmsEntitySnapshot? activeEntity = null,
        CmsDeletionTombstoneSnapshot? tombstone = null,
        CmsEntityRevisionSnapshot? sameVersionRevision = null)
    {
        ArgumentNullException.ThrowIfNull(incomingEvent);

        var inconsistentSnapshot = CheckSnapshotConsistency(
            incomingEvent.EntityId,
            activeEntity,
            tombstone,
            sameVersionRevision);

        if (inconsistentSnapshot is not null)
        {
            return inconsistentSnapshot;
        }

        return incomingEvent switch
        {
            ValidatedPublishEvent publishEvent => DecideVersioned(
                publishEvent.EntityId,
                publishEvent.Version,
                publishEvent.OccurredAtUtc,
                publishEvent.Payload,
                publishEvent.PayloadHash,
                CmsPublicationStatus.Published,
                activeEntity,
                tombstone,
                sameVersionRevision),
            ValidatedUnpublishEvent unpublishEvent => DecideVersioned(
                unpublishEvent.EntityId,
                unpublishEvent.Version,
                unpublishEvent.OccurredAtUtc,
                unpublishEvent.Payload,
                unpublishEvent.PayloadHash,
                CmsPublicationStatus.Unpublished,
                activeEntity,
                tombstone,
                sameVersionRevision),
            ValidatedDeleteEvent deleteEvent => DecideDelete(deleteEvent, activeEntity, tombstone),
            _ => ProcessingDecision.WithoutStateChange(
                ProcessingOutcome.Invalid,
                ProcessingCodes.UnsupportedValidatedEvent),
        };
    }

    private static ProcessingDecision? CheckSnapshotConsistency(
        string entityId,
        ActiveCmsEntitySnapshot? activeEntity,
        CmsDeletionTombstoneSnapshot? tombstone,
        CmsEntityRevisionSnapshot? sameVersionRevision)
    {
        if ((activeEntity is not null && !Matches(entityId, activeEntity.EntityId)) ||
            (tombstone is not null && !Matches(entityId, tombstone.EntityId)) ||
            (sameVersionRevision is not null && !Matches(entityId, sameVersionRevision.EntityId)))
        {
            return ProcessingDecision.WithoutStateChange(
                ProcessingOutcome.Conflict,
                ProcessingCodes.SnapshotEntityMismatch);
        }

        if (activeEntity is not null &&
            tombstone is not null &&
            (activeEntity.Generation <= tombstone.LastDeletedGeneration ||
             activeEntity.CurrentVersionOccurredAtUtc <= tombstone.DeletedAtUtc ||
             activeEntity.EntityEventHighWatermarkUtc <= tombstone.DeletedAtUtc))
        {
            return ProcessingDecision.WithoutStateChange(
                ProcessingOutcome.Conflict,
                ProcessingCodes.SnapshotGenerationConflict);
        }

        if (sameVersionRevision is not null &&
            (activeEntity is null || sameVersionRevision.Generation != activeEntity.Generation))
        {
            return ProcessingDecision.WithoutStateChange(
                ProcessingOutcome.Conflict,
                ProcessingCodes.RevisionSnapshotMismatch);
        }

        return null;
    }

    private static ProcessingDecision DecideVersioned(
        string entityId,
        EntityVersion version,
        UtcTimestamp occurredAtUtc,
        string payload,
        PayloadHash payloadHash,
        CmsPublicationStatus publicationStatus,
        ActiveCmsEntitySnapshot? activeEntity,
        CmsDeletionTombstoneSnapshot? tombstone,
        CmsEntityRevisionSnapshot? sameVersionRevision)
    {
        if (tombstone is not null && occurredAtUtc <= tombstone.DeletedAtUtc)
        {
            return ProcessingDecision.WithoutStateChange(
                ProcessingOutcome.Stale,
                ProcessingCodes.TombstoneBlocked);
        }

        if (activeEntity is null)
        {
            return CreateEntity(
                entityId,
                version,
                occurredAtUtc,
                payload,
                payloadHash,
                publicationStatus,
                tombstone);
        }

        if (version < activeEntity.LatestVersion)
        {
            return ProcessingDecision.WithoutStateChange(
                ProcessingOutcome.Stale,
                ProcessingCodes.VersionStale);
        }

        if (version > activeEntity.LatestVersion)
        {
            return AdvanceVersion(
                entityId,
                version,
                occurredAtUtc,
                payload,
                payloadHash,
                publicationStatus,
                activeEntity);
        }

        return DecideSameVersion(
            occurredAtUtc,
            payloadHash,
            publicationStatus,
            activeEntity,
            sameVersionRevision);
    }

    private static ProcessingDecision CreateEntity(
        string entityId,
        EntityVersion version,
        UtcTimestamp occurredAtUtc,
        string payload,
        PayloadHash payloadHash,
        CmsPublicationStatus publicationStatus,
        CmsDeletionTombstoneSnapshot? tombstone)
    {
        EntityGeneration generation;
        ProcessingCode code;

        if (tombstone is null)
        {
            generation = new EntityGeneration(1);
            code = ProcessingCodes.EntityCreated;
        }
        else
        {
            if (!tombstone.LastDeletedGeneration.TryGetNext(out generation))
            {
                return ProcessingDecision.WithoutStateChange(
                    ProcessingOutcome.Conflict,
                    ProcessingCodes.GenerationExhausted);
            }

            code = ProcessingCodes.EntityRecreated;
        }

        var entity = new ActiveCmsEntitySnapshot(
            entityId,
            generation,
            version,
            payload,
            payloadHash,
            publicationStatus,
            occurredAtUtc,
            occurredAtUtc,
            administrativeDisabled: false);
        var revision = new CmsEntityRevisionSnapshot(
            entityId,
            generation,
            version,
            payload,
            payloadHash,
            occurredAtUtc);

        return ProcessingDecision.Applied(
            code,
            new UpsertActiveEntityOperation(entity),
            new InsertRevisionOperation(revision));
    }

    private static ProcessingDecision AdvanceVersion(
        string entityId,
        EntityVersion version,
        UtcTimestamp occurredAtUtc,
        string payload,
        PayloadHash payloadHash,
        CmsPublicationStatus publicationStatus,
        ActiveCmsEntitySnapshot activeEntity)
    {
        var highWatermark = UtcTimestamp.Max(activeEntity.EntityEventHighWatermarkUtc, occurredAtUtc);
        var entity = new ActiveCmsEntitySnapshot(
            entityId,
            activeEntity.Generation,
            version,
            payload,
            payloadHash,
            publicationStatus,
            occurredAtUtc,
            highWatermark,
            activeEntity.AdministrativeDisabled);
        var revision = new CmsEntityRevisionSnapshot(
            entityId,
            activeEntity.Generation,
            version,
            payload,
            payloadHash,
            occurredAtUtc);

        return ProcessingDecision.Applied(
            ProcessingCodes.VersionAdvanced,
            new InsertRevisionOperation(revision),
            new UpsertActiveEntityOperation(entity));
    }

    private static ProcessingDecision DecideSameVersion(
        UtcTimestamp occurredAtUtc,
        PayloadHash payloadHash,
        CmsPublicationStatus publicationStatus,
        ActiveCmsEntitySnapshot activeEntity,
        CmsEntityRevisionSnapshot? sameVersionRevision)
    {
        if (sameVersionRevision is null)
        {
            return ProcessingDecision.WithoutStateChange(
                ProcessingOutcome.Conflict,
                ProcessingCodes.RevisionSnapshotMissing);
        }

        if (sameVersionRevision.Version != activeEntity.LatestVersion ||
            sameVersionRevision.PayloadHash != activeEntity.PayloadHash)
        {
            return ProcessingDecision.WithoutStateChange(
                ProcessingOutcome.Conflict,
                ProcessingCodes.RevisionSnapshotMismatch);
        }

        if (payloadHash != sameVersionRevision.PayloadHash)
        {
            return ProcessingDecision.WithoutStateChange(
                ProcessingOutcome.Conflict,
                ProcessingCodes.PayloadConflict);
        }

        if (occurredAtUtc < activeEntity.CurrentVersionOccurredAtUtc)
        {
            return ProcessingDecision.WithoutStateChange(
                ProcessingOutcome.Stale,
                ProcessingCodes.EventTimestampStale);
        }

        if (occurredAtUtc == activeEntity.CurrentVersionOccurredAtUtc)
        {
            return publicationStatus == activeEntity.PublicationStatus
                ? ProcessingDecision.WithoutStateChange(
                    ProcessingOutcome.Equivalent,
                    ProcessingCodes.StateEquivalent)
                : ProcessingDecision.WithoutStateChange(
                    ProcessingOutcome.Conflict,
                    ProcessingCodes.PublicationStatusConflict);
        }

        var updatedEntity = new ActiveCmsEntitySnapshot(
            activeEntity.EntityId,
            activeEntity.Generation,
            activeEntity.LatestVersion,
            activeEntity.Payload,
            activeEntity.PayloadHash,
            publicationStatus,
            occurredAtUtc,
            UtcTimestamp.Max(activeEntity.EntityEventHighWatermarkUtc, occurredAtUtc),
            activeEntity.AdministrativeDisabled);

        return ProcessingDecision.Applied(
            ProcessingCodes.SameVersionApplied,
            new UpsertActiveEntityOperation(updatedEntity));
    }

    private static ProcessingDecision DecideDelete(
        ValidatedDeleteEvent deleteEvent,
        ActiveCmsEntitySnapshot? activeEntity,
        CmsDeletionTombstoneSnapshot? tombstone)
    {
        if (activeEntity is not null)
        {
            if (deleteEvent.OccurredAtUtc < activeEntity.EntityEventHighWatermarkUtc)
            {
                return ProcessingDecision.WithoutStateChange(
                    ProcessingOutcome.Stale,
                    ProcessingCodes.DeleteStale);
            }

            if (deleteEvent.OccurredAtUtc == activeEntity.EntityEventHighWatermarkUtc)
            {
                return ProcessingDecision.WithoutStateChange(
                    ProcessingOutcome.Conflict,
                    ProcessingCodes.DeleteConflict);
            }

            var advancedTombstone = new CmsDeletionTombstoneSnapshot(
                deleteEvent.EntityId,
                activeEntity.Generation,
                deleteEvent.OccurredAtUtc);

            return ProcessingDecision.Applied(
                ProcessingCodes.EntityDeleted,
                new DeleteAllRevisionsOperation(deleteEvent.EntityId),
                new DeleteActiveEntityOperation(deleteEvent.EntityId),
                new UpsertDeletionTombstoneOperation(advancedTombstone));
        }

        if (tombstone is null)
        {
            var initialTombstone = new CmsDeletionTombstoneSnapshot(
                deleteEvent.EntityId,
                new EntityGeneration(0),
                deleteEvent.OccurredAtUtc);

            return ProcessingDecision.Applied(
                ProcessingCodes.TombstoneCreated,
                new UpsertDeletionTombstoneOperation(initialTombstone));
        }

        if (deleteEvent.OccurredAtUtc < tombstone.DeletedAtUtc)
        {
            return ProcessingDecision.WithoutStateChange(
                ProcessingOutcome.Stale,
                ProcessingCodes.TombstoneStale);
        }

        if (deleteEvent.OccurredAtUtc == tombstone.DeletedAtUtc)
        {
            return ProcessingDecision.WithoutStateChange(
                ProcessingOutcome.Equivalent,
                ProcessingCodes.TombstoneEquivalent);
        }

        var updatedTombstone = new CmsDeletionTombstoneSnapshot(
            deleteEvent.EntityId,
            tombstone.LastDeletedGeneration,
            deleteEvent.OccurredAtUtc);

        return ProcessingDecision.Applied(
            ProcessingCodes.TombstoneAdvanced,
            new UpsertDeletionTombstoneOperation(updatedTombstone));
    }

    private static bool Matches(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}
