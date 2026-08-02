namespace CmsSync.Infrastructure.Persistence;

internal static class PersistenceIndexFilters
{
    public const string CmsEventProcessingLogsIdempotencyOwner =
        "[OwnsIdempotencyKey] = CAST(1 AS bit) AND [IdempotencyKey] IS NOT NULL";
}
