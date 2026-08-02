namespace CmsSync.Infrastructure.Persistence;

public static class PersistenceIndexNames
{
    public const string CmsEventProcessingLogsBatchIdSequence =
        "UX_CmsEventProcessingLogs_BatchId_Sequence";
    public const string CmsEventProcessingLogsIdempotencyOwner =
        "UX_CmsEventProcessingLogs_IdempotencyOwner";
}
