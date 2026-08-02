using CmsSync.Infrastructure.Persistence;
using CmsSync.Infrastructure.Persistence.Models;
using CmsSync.IntegrationTests.Infrastructure;
using Xunit;

namespace CmsSync.IntegrationTests.Persistence.SqlServer;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "SqlServer")]
public sealed class SchemaMetadataTests
{
    private readonly SqlServerFixture _fixture;

    public SchemaMetadataTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void PinnedImageRunsSqlServer2022DeveloperEdition()
    {
        Assert.Equal("16.0.4265.3", _fixture.ProductVersion);
        Assert.Equal("Developer Edition (64-bit)", _fixture.Edition);
    }

    [Fact]
    public async Task PhysicalTablesColumnsAndTypesMatchTheApprovedSchema()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tables = await SqlServerMetadataReader.ReadApplicationTablesAsync(
            _fixture.WriteConnectionString,
            cancellationToken);
        var columns = await SqlServerMetadataReader.ReadColumnsAsync(
            _fixture.WriteConnectionString,
            cancellationToken);
        var columnMap = columns.ToDictionary(
            column => (column.TableName, column.ColumnName),
            column => column);

        Assert.Equal(
            new[]
            {
                PersistenceModelConstants.CmsDeletionTombstonesTable,
                PersistenceModelConstants.CmsEntitiesTable,
                PersistenceModelConstants.CmsEntityRevisionsTable,
                PersistenceModelConstants.CmsEventProcessingLogsTable,
            }.Order(StringComparer.Ordinal),
            tables);

        AssertIdentifier(columnMap, PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.EntityId));
        AssertColumn(columnMap, PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.Generation), "bigint", false);
        AssertColumn(columnMap, PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.LatestVersion), "bigint", false);
        AssertColumn(columnMap, PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.Payload), "nvarchar", false, -1);
        AssertColumn(columnMap, PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.PayloadHash), "binary", false, 32);
        AssertCategorical(columnMap, PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.CmsPublicationStatus), 16);
        AssertDateTime(columnMap, PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.CurrentVersionOccurredAtUtc), false);
        AssertDateTime(columnMap, PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.EntityEventHighWatermarkUtc), false);
        AssertColumn(columnMap, PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.AdministrativeDisabled), "bit", false);
        AssertDateTime(columnMap, PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.AdministrativeStateChangedAtUtc), true);
        AssertIdentifier(columnMap, PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.AdministrativeStateChangedBy), true);
        AssertDateTime(columnMap, PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.CreatedAtUtc), false);
        AssertDateTime(columnMap, PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.UpdatedAtUtc), false);
        AssertRowVersion(columnMap, PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.RowVersion));

        AssertIdentifier(columnMap, PersistenceModelConstants.CmsEntityRevisionsTable, nameof(CmsEntityRevision.EntityId));
        AssertColumn(columnMap, PersistenceModelConstants.CmsEntityRevisionsTable, nameof(CmsEntityRevision.Generation), "bigint", false);
        AssertColumn(columnMap, PersistenceModelConstants.CmsEntityRevisionsTable, nameof(CmsEntityRevision.Version), "bigint", false);
        AssertColumn(columnMap, PersistenceModelConstants.CmsEntityRevisionsTable, nameof(CmsEntityRevision.FirstObservedPayload), "nvarchar", false, -1);
        AssertColumn(columnMap, PersistenceModelConstants.CmsEntityRevisionsTable, nameof(CmsEntityRevision.PayloadHash), "binary", false, 32);
        AssertDateTime(columnMap, PersistenceModelConstants.CmsEntityRevisionsTable, nameof(CmsEntityRevision.FirstObservedAtUtc), false);
        Assert.DoesNotContain(
            columns,
            column => column.TableName == PersistenceModelConstants.CmsEntityRevisionsTable &&
                column.ColumnName.Contains("PublicationStatus", StringComparison.Ordinal));

        AssertIdentifier(columnMap, PersistenceModelConstants.CmsDeletionTombstonesTable, nameof(CmsDeletionTombstone.EntityId));
        AssertColumn(columnMap, PersistenceModelConstants.CmsDeletionTombstonesTable, nameof(CmsDeletionTombstone.LastDeletedGeneration), "bigint", false);
        AssertDateTime(columnMap, PersistenceModelConstants.CmsDeletionTombstonesTable, nameof(CmsDeletionTombstone.DeletedAtUtc), false);
        AssertIdentifier(columnMap, PersistenceModelConstants.CmsDeletionTombstonesTable, nameof(CmsDeletionTombstone.LastDeleteEventKey), true, 418);
        AssertDateTime(columnMap, PersistenceModelConstants.CmsDeletionTombstonesTable, nameof(CmsDeletionTombstone.CreatedAtUtc), false);
        AssertDateTime(columnMap, PersistenceModelConstants.CmsDeletionTombstonesTable, nameof(CmsDeletionTombstone.UpdatedAtUtc), false);
        AssertRowVersion(columnMap, PersistenceModelConstants.CmsDeletionTombstonesTable, nameof(CmsDeletionTombstone.RowVersion));

        AssertProcessingLogColumns(columnMap);

        var currentTimestamp = columnMap[(PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.CurrentVersionOccurredAtUtc))];
        var highWatermark = columnMap[(PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.EntityEventHighWatermarkUtc))];
        Assert.NotEqual(currentTimestamp.ColumnName, highWatermark.ColumnName);

        var prohibitedFragments = new[]
        {
            "RawPayload",
            "RequestBody",
            "Authorization",
            "Password",
            "Credential",
            "StackTrace",
        };
        Assert.DoesNotContain(
            columns.Where(column =>
                column.TableName == PersistenceModelConstants.CmsDeletionTombstonesTable ||
                column.TableName == PersistenceModelConstants.CmsEventProcessingLogsTable),
            column => prohibitedFragments.Any(fragment =>
                column.ColumnName.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task PhysicalKeysConstraintsIndexesAndReplayForeignKeyMatchExactly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var primaryKeys = await SqlServerMetadataReader.ReadPrimaryKeyColumnsAsync(
            _fixture.WriteConnectionString,
            cancellationToken);
        var constraints = await SqlServerMetadataReader.ReadCheckConstraintsAsync(
            _fixture.WriteConnectionString,
            cancellationToken);
        var indexColumns = await SqlServerMetadataReader.ReadIndexColumnsAsync(
            _fixture.WriteConnectionString,
            cancellationToken);
        var foreignKeys = await SqlServerMetadataReader.ReadForeignKeysAsync(
            _fixture.WriteConnectionString,
            cancellationToken);

        AssertPrimaryKey(primaryKeys, PersistenceModelConstants.CmsEntitiesTable, "PK_CmsEntities", nameof(CmsEntity.EntityId));
        AssertPrimaryKey(
            primaryKeys,
            PersistenceModelConstants.CmsEntityRevisionsTable,
            "PK_CmsEntityRevisions",
            nameof(CmsEntityRevision.EntityId),
            nameof(CmsEntityRevision.Generation),
            nameof(CmsEntityRevision.Version));
        AssertPrimaryKey(
            primaryKeys,
            PersistenceModelConstants.CmsDeletionTombstonesTable,
            "PK_CmsDeletionTombstones",
            nameof(CmsDeletionTombstone.EntityId));
        AssertPrimaryKey(
            primaryKeys,
            PersistenceModelConstants.CmsEventProcessingLogsTable,
            "PK_CmsEventProcessingLogs",
            nameof(CmsEventProcessingLog.ProcessingLogId));
        Assert.Equal(4, primaryKeys.Select(key => key.ConstraintName).Distinct(StringComparer.Ordinal).Count());

        var expectedConstraintNames = new[]
        {
            PersistenceConstraintNames.CmsEntitiesGenerationPositive,
            PersistenceConstraintNames.CmsEntitiesLatestVersionPositive,
            PersistenceConstraintNames.CmsEntitiesPayloadJsonObject,
            PersistenceConstraintNames.CmsEntitiesPublicationStatus,
            PersistenceConstraintNames.CmsEntitiesEventTimestamps,
            PersistenceConstraintNames.CmsEntitiesAdministrativeAudit,
            PersistenceConstraintNames.CmsEntityRevisionsGenerationPositive,
            PersistenceConstraintNames.CmsEntityRevisionsVersionPositive,
            PersistenceConstraintNames.CmsEntityRevisionsPayloadJsonObject,
            PersistenceConstraintNames.CmsDeletionTombstonesGenerationNonNegative,
            PersistenceConstraintNames.CmsEventProcessingLogsSequenceNonNegative,
            PersistenceConstraintNames.CmsEventProcessingLogsIdempotencyOwner,
            PersistenceConstraintNames.CmsEventProcessingLogsReplayDoesNotOwnIdentity,
            PersistenceConstraintNames.CmsEventProcessingLogsEventType,
            PersistenceConstraintNames.CmsEventProcessingLogsOutcome,
            PersistenceConstraintNames.CmsEventProcessingLogsVersionPositive,
            PersistenceConstraintNames.CmsEventProcessingLogsGenerationNonNegative,
            PersistenceConstraintNames.CmsEventProcessingLogsResultingVersionPositive,
        };

        Assert.Equal(18, constraints.Count);
        Assert.Equal(
            expectedConstraintNames.Order(StringComparer.Ordinal),
            constraints.Select(constraint => constraint.ConstraintName).Order(StringComparer.Ordinal));
        Assert.All(constraints, constraint => Assert.False(string.IsNullOrWhiteSpace(constraint.Definition)));

        var nonPrimaryIndexes = indexColumns
            .Where(index => !index.IsPrimaryKey)
            .GroupBy(index => (index.TableName, index.IndexName))
            .ToArray();
        Assert.Equal(3, nonPrimaryIndexes.Length);
        AssertIndex(
            nonPrimaryIndexes,
            "IX_CmsEventProcessingLogs_ReplayOfProcessingLogId",
            false,
            null,
            nameof(CmsEventProcessingLog.ReplayOfProcessingLogId));
        AssertIndex(
            nonPrimaryIndexes,
            PersistenceIndexNames.CmsEventProcessingLogsBatchIdSequence,
            true,
            null,
            nameof(CmsEventProcessingLog.BatchId),
            nameof(CmsEventProcessingLog.Sequence));
        AssertIndex(
            nonPrimaryIndexes,
            PersistenceIndexNames.CmsEventProcessingLogsIdempotencyOwner,
            true,
            "([OwnsIdempotencyKey]=CONVERT([bit],(1)) AND [IdempotencyKey] IS NOT NULL)",
            nameof(CmsEventProcessingLog.IdempotencyKey));

        var replayForeignKey = Assert.Single(foreignKeys);
        Assert.Equal(
            "FK_CmsEventProcessingLogs_CmsEventProcessingLogs_ReplayOfProcessingLogId",
            replayForeignKey.ForeignKeyName);
        Assert.Equal(PersistenceModelConstants.CmsEventProcessingLogsTable, replayForeignKey.ParentTable);
        Assert.Equal(nameof(CmsEventProcessingLog.ReplayOfProcessingLogId), replayForeignKey.ParentColumn);
        Assert.Equal(PersistenceModelConstants.CmsEventProcessingLogsTable, replayForeignKey.ReferencedTable);
        Assert.Equal(nameof(CmsEventProcessingLog.ProcessingLogId), replayForeignKey.ReferencedColumn);
        Assert.Equal("NO_ACTION", replayForeignKey.DeleteAction);
    }

    private static void AssertProcessingLogColumns(
        Dictionary<(string TableName, string ColumnName), (
            string TableName,
            string ColumnName,
            string TypeName,
            short MaximumLength,
            byte Precision,
            byte Scale,
            bool IsNullable,
            bool IsIdentity,
            string? Collation)> columns)
    {
        var table = PersistenceModelConstants.CmsEventProcessingLogsTable;
        AssertColumn(columns, table, nameof(CmsEventProcessingLog.ProcessingLogId), "bigint", false);
        Assert.True(columns[(table, nameof(CmsEventProcessingLog.ProcessingLogId))].IsIdentity);
        AssertColumn(columns, table, nameof(CmsEventProcessingLog.BatchId), "uniqueidentifier", false);
        AssertColumn(columns, table, nameof(CmsEventProcessingLog.Sequence), "int", false);
        AssertIdentifier(columns, table, nameof(CmsEventProcessingLog.IdempotencyKey), true, 418);
        AssertColumn(columns, table, nameof(CmsEventProcessingLog.OwnsIdempotencyKey), "bit", false);
        AssertColumn(columns, table, nameof(CmsEventProcessingLog.ReplayOfProcessingLogId), "bigint", true);
        AssertIdentifier(columns, table, nameof(CmsEventProcessingLog.ExternalEventId), true);
        AssertColumn(columns, table, nameof(CmsEventProcessingLog.EventContentHash), "binary", true, 32);
        AssertColumn(columns, table, nameof(CmsEventProcessingLog.PayloadHash), "binary", true, 32);
        AssertCategorical(columns, table, nameof(CmsEventProcessingLog.EventType), 16, true);
        AssertIdentifier(columns, table, nameof(CmsEventProcessingLog.EntityId), true);
        AssertColumn(columns, table, nameof(CmsEventProcessingLog.Version), "bigint", true);
        AssertDateTime(columns, table, nameof(CmsEventProcessingLog.EventOccurredAtUtc), true);
        AssertCategorical(columns, table, nameof(CmsEventProcessingLog.Outcome), 16);
        AssertCategorical(columns, table, nameof(CmsEventProcessingLog.Code), 100);
        AssertColumn(columns, table, nameof(CmsEventProcessingLog.Generation), "bigint", true);
        AssertColumn(columns, table, nameof(CmsEventProcessingLog.ResultingVersion), "bigint", true);
        AssertDateTime(columns, table, nameof(CmsEventProcessingLog.ProcessedAtUtc), false);
        AssertIdentifier(columns, table, nameof(CmsEventProcessingLog.CorrelationId));
        AssertIdentifier(columns, table, nameof(CmsEventProcessingLog.AuthenticatedCmsSubject));
    }

    private static void AssertColumn(
        Dictionary<(string TableName, string ColumnName), (
            string TableName,
            string ColumnName,
            string TypeName,
            short MaximumLength,
            byte Precision,
            byte Scale,
            bool IsNullable,
            bool IsIdentity,
            string? Collation)> columns,
        string tableName,
        string columnName,
        string typeName,
        bool nullable,
        short? maximumLength = null)
    {
        var column = columns[(tableName, columnName)];
        Assert.Equal(typeName, column.TypeName);
        Assert.Equal(nullable, column.IsNullable);

        if (columnName != nameof(CmsEventProcessingLog.ProcessingLogId))
        {
            Assert.False(column.IsIdentity);
        }

        if (maximumLength.HasValue)
        {
            Assert.Equal(maximumLength.Value, column.MaximumLength);
        }
    }

    private static void AssertIdentifier(
        Dictionary<(string TableName, string ColumnName), (
            string TableName,
            string ColumnName,
            string TypeName,
            short MaximumLength,
            byte Precision,
            byte Scale,
            bool IsNullable,
            bool IsIdentity,
            string? Collation)> columns,
        string tableName,
        string columnName,
        bool nullable = false,
        short maximumLength = 400)
    {
        var column = columns[(tableName, columnName)];
        Assert.Equal("nvarchar", column.TypeName);
        Assert.Equal(maximumLength, column.MaximumLength);
        Assert.Equal(nullable, column.IsNullable);
        Assert.Equal(PersistenceModelConstants.CaseSensitiveCollation, column.Collation);
    }

    private static void AssertCategorical(
        Dictionary<(string TableName, string ColumnName), (
            string TableName,
            string ColumnName,
            string TypeName,
            short MaximumLength,
            byte Precision,
            byte Scale,
            bool IsNullable,
            bool IsIdentity,
            string? Collation)> columns,
        string tableName,
        string columnName,
        short maximumLength,
        bool nullable = false)
    {
        var column = columns[(tableName, columnName)];
        Assert.Equal("varchar", column.TypeName);
        Assert.Equal(maximumLength, column.MaximumLength);
        Assert.Equal(nullable, column.IsNullable);
        Assert.Equal(PersistenceModelConstants.CaseSensitiveCollation, column.Collation);
    }

    private static void AssertDateTime(
        Dictionary<(string TableName, string ColumnName), (
            string TableName,
            string ColumnName,
            string TypeName,
            short MaximumLength,
            byte Precision,
            byte Scale,
            bool IsNullable,
            bool IsIdentity,
            string? Collation)> columns,
        string tableName,
        string columnName,
        bool nullable)
    {
        var column = columns[(tableName, columnName)];
        Assert.Equal("datetime2", column.TypeName);
        Assert.Equal(7, column.Scale);
        Assert.Equal(nullable, column.IsNullable);
    }

    private static void AssertRowVersion(
        Dictionary<(string TableName, string ColumnName), (
            string TableName,
            string ColumnName,
            string TypeName,
            short MaximumLength,
            byte Precision,
            byte Scale,
            bool IsNullable,
            bool IsIdentity,
            string? Collation)> columns,
        string tableName,
        string columnName)
    {
        var column = columns[(tableName, columnName)];
        Assert.Equal("timestamp", column.TypeName);
        Assert.Equal(8, column.MaximumLength);
        Assert.False(column.IsNullable);
    }

    private static void AssertPrimaryKey(
        IEnumerable<(string TableName, string ConstraintName, int KeyOrdinal, string ColumnName)> primaryKeys,
        string tableName,
        string constraintName,
        params string[] columnNames)
    {
        var rows = primaryKeys
            .Where(key => key.TableName == tableName)
            .OrderBy(key => key.KeyOrdinal)
            .ToArray();

        Assert.All(rows, row => Assert.Equal(constraintName, row.ConstraintName));
        Assert.Equal(columnNames, rows.Select(row => row.ColumnName));
    }

    private static void AssertIndex(
        IEnumerable<IGrouping<(string TableName, string IndexName), (
            string TableName,
            string IndexName,
            bool IsUnique,
            bool IsPrimaryKey,
            bool IsUniqueConstraint,
            string? FilterDefinition,
            int KeyOrdinal,
            string ColumnName)>> indexes,
        string indexName,
        bool unique,
        string? filter,
        params string[] columnNames)
    {
        var index = Assert.Single(indexes, candidate => candidate.Key.IndexName == indexName);
        var rows = index.OrderBy(row => row.KeyOrdinal).ToArray();
        Assert.All(rows, row => Assert.Equal(unique, row.IsUnique));
        Assert.All(rows, row => Assert.False(row.IsPrimaryKey));
        Assert.All(rows, row => Assert.False(row.IsUniqueConstraint));
        Assert.All(rows, row => Assert.Equal(filter, row.FilterDefinition));
        Assert.Equal(columnNames, rows.Select(row => row.ColumnName));
    }
}
