namespace CmsSync.Infrastructure.Persistence;

public static class PersistenceModelConstants
{
    public const string CmsEntitiesTable = "CmsEntities";
    public const string CmsEntityRevisionsTable = "CmsEntityRevisions";
    public const string CmsDeletionTombstonesTable = "CmsDeletionTombstones";
    public const string CmsEventProcessingLogsTable = "CmsEventProcessingLogs";

    public const string CaseSensitiveCollation = "Latin1_General_100_BIN2";
    public const string DateTimeColumnType = "datetime2(7)";
    public const string PayloadColumnType = "nvarchar(max)";
    public const string HashColumnType = "binary(32)";

    public const int EntityIdentifierMaxLength = 200;
    public const int ExternalEventIdentifierMaxLength = 200;
    public const int IdempotencyKeyMaxLength = 209;
    public const int AdministrativeSubjectMaxLength = 200;
    public const int CorrelationIdentifierMaxLength = 200;
    public const int CmsSubjectIdentifierMaxLength = 200;
    public const int PublicationStatusMaxLength = 16;
    public const int EventTypeMaxLength = 16;
    public const int ProcessingOutcomeMaxLength = 16;
    public const int ProcessingCodeMaxLength = 100;
    public const int HashLength = 32;

    public static string CreateJsonObjectCheck(string columnName) =>
        $"ISJSON([{columnName}], OBJECT) = 1";
}
