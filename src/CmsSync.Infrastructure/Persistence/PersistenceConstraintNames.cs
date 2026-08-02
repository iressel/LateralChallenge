namespace CmsSync.Infrastructure.Persistence;

public static class PersistenceConstraintNames
{
    public const string CmsEntitiesGenerationPositive = "CK_CmsEntities_Generation_Positive";
    public const string CmsEntitiesLatestVersionPositive = "CK_CmsEntities_LatestVersion_Positive";
    public const string CmsEntitiesPayloadJsonObject = "CK_CmsEntities_Payload_JsonObject";
    public const string CmsEntitiesPublicationStatus = "CK_CmsEntities_PublicationStatus";
    public const string CmsEntitiesEventTimestamps = "CK_CmsEntities_EventTimestamps";
    public const string CmsEntitiesAdministrativeAudit = "CK_CmsEntities_AdministrativeAudit";

    public const string CmsEntityRevisionsGenerationPositive = "CK_CmsEntityRevisions_Generation_Positive";
    public const string CmsEntityRevisionsVersionPositive = "CK_CmsEntityRevisions_Version_Positive";
    public const string CmsEntityRevisionsPayloadJsonObject = "CK_CmsEntityRevisions_Payload_JsonObject";

    public const string CmsDeletionTombstonesGenerationNonNegative =
        "CK_CmsDeletionTombstones_Generation_NonNegative";

    public const string CmsEventProcessingLogsSequenceNonNegative =
        "CK_CmsEventProcessingLogs_Sequence_NonNegative";
    public const string CmsEventProcessingLogsIdempotencyOwner =
        "CK_CmsEventProcessingLogs_IdempotencyOwner";
    public const string CmsEventProcessingLogsReplayDoesNotOwnIdentity =
        "CK_CmsEventProcessingLogs_ReplayDoesNotOwnIdentity";
    public const string CmsEventProcessingLogsEventType = "CK_CmsEventProcessingLogs_EventType";
    public const string CmsEventProcessingLogsOutcome = "CK_CmsEventProcessingLogs_Outcome";
    public const string CmsEventProcessingLogsVersionPositive =
        "CK_CmsEventProcessingLogs_Version_Positive";
    public const string CmsEventProcessingLogsGenerationNonNegative =
        "CK_CmsEventProcessingLogs_Generation_NonNegative";
    public const string CmsEventProcessingLogsResultingVersionPositive =
        "CK_CmsEventProcessingLogs_ResultingVersion_Positive";
}
