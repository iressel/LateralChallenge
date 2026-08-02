using System.Reflection;
using CmsSync.Infrastructure.Persistence;
using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace CmsSync.IntegrationTests.Persistence.Migrations;

public sealed class InitialCmsPersistenceMigrationTests
{
    private const string MigrationId = "20260802142305_InitialCmsPersistence";

    private static readonly string[] ExpectedTableNames =
    {
        PersistenceModelConstants.CmsDeletionTombstonesTable,
        PersistenceModelConstants.CmsEntitiesTable,
        PersistenceModelConstants.CmsEntityRevisionsTable,
        PersistenceModelConstants.CmsEventProcessingLogsTable,
    };

    [Fact]
    public void MigrationAssemblyContainsOnlyTheInitialWriteContextMigration()
    {
        using var writeContext = MigrationTestContext.CreateWriteContext();
        using var readContext = MigrationTestContext.CreateReadContext();
        var writeMigrationsAssembly = MigrationTestContext.GetMigrationsAssembly(writeContext);
        var migrationEntry = Assert.Single(writeMigrationsAssembly.Migrations);
        var writeContextAttribute = migrationEntry.Value.GetCustomAttribute<DbContextAttribute>();

        Assert.Equal(MigrationId, migrationEntry.Key);
        Assert.Equal(typeof(CmsWriteDbContext), writeContextAttribute?.ContextType);
        Assert.NotNull(writeMigrationsAssembly.ModelSnapshot);
        Assert.Equal(
            typeof(CmsWriteDbContext),
            writeMigrationsAssembly.ModelSnapshot!.GetType().GetCustomAttribute<DbContextAttribute>()?.ContextType);

        var readMigrationsAssembly = MigrationTestContext.GetMigrationsAssembly(readContext);
        Assert.Empty(readMigrationsAssembly.Migrations);
        Assert.Null(readMigrationsAssembly.ModelSnapshot);
    }

    [Fact]
    public void UpCreatesExactlyTheFourRequiredApplicationTables()
    {
        var migration = GetMigration();
        var createdTableNames = migration.UpOperations
            .OfType<CreateTableOperation>()
            .Select(operation => operation.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedTableNames.Order(StringComparer.Ordinal), createdTableNames);
    }

    [Fact]
    public void DownDropsExactlyTheFourRequiredApplicationTables()
    {
        var migration = GetMigration();
        var droppedTableNames = migration.DownOperations
            .OfType<DropTableOperation>()
            .Select(operation => operation.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedTableNames.Order(StringComparer.Ordinal), droppedTableNames);
        Assert.Equal(4, migration.DownOperations.Count);
    }

    [Fact]
    public void CmsEntitiesKeepsBothRequiredDatetimeColumnsSeparate()
    {
        var table = GetCreateTable(GetMigration(), PersistenceModelConstants.CmsEntitiesTable);
        var currentVersionTimestamp = GetColumn(table, nameof(CmsEntity.CurrentVersionOccurredAtUtc));
        var highWatermarkTimestamp = GetColumn(table, nameof(CmsEntity.EntityEventHighWatermarkUtc));

        Assert.Equal(PersistenceModelConstants.DateTimeColumnType, currentVersionTimestamp.ColumnType);
        Assert.Equal(PersistenceModelConstants.DateTimePrecision, currentVersionTimestamp.Precision);
        Assert.False(currentVersionTimestamp.IsNullable);
        Assert.Equal(PersistenceModelConstants.DateTimeColumnType, highWatermarkTimestamp.ColumnType);
        Assert.Equal(PersistenceModelConstants.DateTimePrecision, highWatermarkTimestamp.Precision);
        Assert.False(highWatermarkTimestamp.IsNullable);
        Assert.NotEqual(currentVersionTimestamp.Name, highWatermarkTimestamp.Name);
    }

    [Fact]
    public void TablesHaveTheExpectedPrimaryKeys()
    {
        var migration = GetMigration();

        AssertPrimaryKey(
            migration,
            PersistenceModelConstants.CmsEntitiesTable,
            nameof(CmsEntity.EntityId));
        AssertPrimaryKey(
            migration,
            PersistenceModelConstants.CmsEntityRevisionsTable,
            nameof(CmsEntityRevision.EntityId),
            nameof(CmsEntityRevision.Generation),
            nameof(CmsEntityRevision.Version));
        AssertPrimaryKey(
            migration,
            PersistenceModelConstants.CmsDeletionTombstonesTable,
            nameof(CmsDeletionTombstone.EntityId));
        AssertPrimaryKey(
            migration,
            PersistenceModelConstants.CmsEventProcessingLogsTable,
            nameof(CmsEventProcessingLog.ProcessingLogId));
    }

    [Fact]
    public void MigrationContainsEveryExpectedCheckConstraintName()
    {
        var migration = GetMigration();
        var actualNames = migration.UpOperations
            .OfType<CreateTableOperation>()
            .SelectMany(operation => operation.CheckConstraints)
            .Select(constraint => constraint.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedNames = new[]
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

        Assert.Equal(expectedNames.Order(StringComparer.Ordinal), actualNames);
    }

    [Fact]
    public void ProcessingLogIndexesAndReplayReferencePreserveIdentityRules()
    {
        var migration = GetMigration();
        var indexes = migration.UpOperations.OfType<CreateIndexOperation>().ToArray();
        var indexNames = indexes.Select(index => index.Name).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            new[]
            {
                "IX_CmsEventProcessingLogs_ReplayOfProcessingLogId",
                PersistenceIndexNames.CmsEventProcessingLogsBatchIdSequence,
                PersistenceIndexNames.CmsEventProcessingLogsIdempotencyOwner,
            }.Order(StringComparer.Ordinal),
            indexNames);

        var batchIndex = Assert.Single(
            indexes,
            index => string.Equals(
                index.Name,
                PersistenceIndexNames.CmsEventProcessingLogsBatchIdSequence,
                StringComparison.Ordinal));
        Assert.True(batchIndex.IsUnique);
        Assert.Equal(new[] { nameof(CmsEventProcessingLog.BatchId), nameof(CmsEventProcessingLog.Sequence) }, batchIndex.Columns);

        var ownerIndex = Assert.Single(
            indexes,
            index => string.Equals(
                index.Name,
                PersistenceIndexNames.CmsEventProcessingLogsIdempotencyOwner,
                StringComparison.Ordinal));
        Assert.True(ownerIndex.IsUnique);
        Assert.Equal(new[] { nameof(CmsEventProcessingLog.IdempotencyKey) }, ownerIndex.Columns);
        Assert.Equal(
            "[OwnsIdempotencyKey] = CAST(1 AS bit) AND [IdempotencyKey] IS NOT NULL",
            ownerIndex.Filter);

        var processingTable = GetCreateTable(migration, PersistenceModelConstants.CmsEventProcessingLogsTable);
        var replayForeignKey = Assert.Single(processingTable.ForeignKeys);
        Assert.Equal(PersistenceModelConstants.CmsEventProcessingLogsTable, replayForeignKey.PrincipalTable);
        Assert.Equal(
            new[] { nameof(CmsEventProcessingLog.ReplayOfProcessingLogId) },
            replayForeignKey.Columns);
        Assert.Equal(ReferentialAction.NoAction, replayForeignKey.OnDelete);
    }

    [Fact]
    public void CriticalColumnsKeepExplicitCollationsHashesAndRowVersions()
    {
        var migration = GetMigration();
        var collatedColumns = new Dictionary<string, string[]>
        {
            [PersistenceModelConstants.CmsEntitiesTable] =
            [
                nameof(CmsEntity.EntityId),
                nameof(CmsEntity.CmsPublicationStatus),
            ],
            [PersistenceModelConstants.CmsEntityRevisionsTable] =
            [
                nameof(CmsEntityRevision.EntityId),
            ],
            [PersistenceModelConstants.CmsDeletionTombstonesTable] =
            [
                nameof(CmsDeletionTombstone.EntityId),
                nameof(CmsDeletionTombstone.LastDeleteEventKey),
            ],
            [PersistenceModelConstants.CmsEventProcessingLogsTable] =
            [
                nameof(CmsEventProcessingLog.IdempotencyKey),
                nameof(CmsEventProcessingLog.ExternalEventId),
                nameof(CmsEventProcessingLog.EventType),
                nameof(CmsEventProcessingLog.EntityId),
                nameof(CmsEventProcessingLog.Outcome),
                nameof(CmsEventProcessingLog.Code),
            ],
        };

        foreach (var (tableName, columnNames) in collatedColumns)
        {
            var table = GetCreateTable(migration, tableName);

            foreach (var columnName in columnNames)
            {
                Assert.Equal(
                    PersistenceModelConstants.CaseSensitiveCollation,
                    GetColumn(table, columnName).Collation);
            }
        }

        AssertHash(migration, PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.PayloadHash), nullable: false);
        AssertHash(
            migration,
            PersistenceModelConstants.CmsEntityRevisionsTable,
            nameof(CmsEntityRevision.PayloadHash),
            nullable: false);
        AssertHash(
            migration,
            PersistenceModelConstants.CmsEventProcessingLogsTable,
            nameof(CmsEventProcessingLog.EventContentHash),
            nullable: true);
        AssertHash(
            migration,
            PersistenceModelConstants.CmsEventProcessingLogsTable,
            nameof(CmsEventProcessingLog.PayloadHash),
            nullable: true);
        AssertRowVersion(migration, PersistenceModelConstants.CmsEntitiesTable, nameof(CmsEntity.RowVersion));
        AssertRowVersion(
            migration,
            PersistenceModelConstants.CmsDeletionTombstonesTable,
            nameof(CmsDeletionTombstone.RowVersion));
    }

    [Fact]
    public void TombstoneAndProcessingLogContainNoProhibitedPayloadOrAuthenticationColumns()
    {
        var migration = GetMigration();
        var tombstoneColumns = GetCreateTable(
            migration,
            PersistenceModelConstants.CmsDeletionTombstonesTable).Columns.Select(column => column.Name).ToArray();
        var processingLogColumns = GetCreateTable(
            migration,
            PersistenceModelConstants.CmsEventProcessingLogsTable).Columns.Select(column => column.Name).ToArray();

        Assert.DoesNotContain(nameof(CmsEntity.Payload), tombstoneColumns, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(CmsEventProcessingLog.EventContentHash), tombstoneColumns, StringComparer.OrdinalIgnoreCase);

        foreach (var prohibitedColumn in new[]
                 {
                     "Payload",
                     "RawPayload",
                     "RequestBody",
                     "Authorization",
                     "AuthorizationHeader",
                     "Password",
                     "Credential",
                     "ConnectionString",
                     "ExceptionStackTrace",
                     "DiagnosticText",
                 })
        {
            Assert.DoesNotContain(prohibitedColumn, tombstoneColumns, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(prohibitedColumn, processingLogColumns, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ProcessingLogPrimaryKeyUsesSqlServerIdentity()
    {
        var migration = GetMigration();
        var table = GetCreateTable(migration, PersistenceModelConstants.CmsEventProcessingLogsTable);
        var primaryKeyColumn = GetColumn(table, nameof(CmsEventProcessingLog.ProcessingLogId));

        Assert.Equal("1, 1", primaryKeyColumn["SqlServer:Identity"]);
    }

    [Fact]
    public void CurrentModelHasNoPendingChangesAfterInitialMigration()
    {
        using var context = MigrationTestContext.CreateWriteContext();
        var migrator = context.GetService<IMigrator>();

        Assert.False(migrator.HasPendingModelChanges());
    }

    private static Migration GetMigration()
    {
        using var context = MigrationTestContext.CreateWriteContext();
        var migrationEntry = Assert.Single(MigrationTestContext.GetMigrationsAssembly(context).Migrations);

        return MigrationTestContext.CreateMigration(context, migrationEntry);
    }

    private static CreateTableOperation GetCreateTable(Migration migration, string tableName)
    {
        return Assert.Single(
            migration.UpOperations.OfType<CreateTableOperation>(),
            operation => string.Equals(operation.Name, tableName, StringComparison.Ordinal));
    }

    private static AddColumnOperation GetColumn(CreateTableOperation table, string columnName)
    {
        return Assert.Single(
            table.Columns,
            column => string.Equals(column.Name, columnName, StringComparison.Ordinal));
    }

    private static void AssertPrimaryKey(Migration migration, string tableName, params string[] columnNames)
    {
        var primaryKey = GetCreateTable(migration, tableName).PrimaryKey;

        Assert.NotNull(primaryKey);
        Assert.Equal(columnNames, primaryKey.Columns);
    }

    private static void AssertHash(Migration migration, string tableName, string columnName, bool nullable)
    {
        var column = GetColumn(GetCreateTable(migration, tableName), columnName);

        Assert.Equal(PersistenceModelConstants.HashColumnType, column.ColumnType);
        Assert.Equal(PersistenceModelConstants.HashLength, column.MaxLength);
        Assert.True(column.IsFixedLength);
        Assert.Equal(nullable, column.IsNullable);
    }

    private static void AssertRowVersion(Migration migration, string tableName, string columnName)
    {
        var column = GetColumn(GetCreateTable(migration, tableName), columnName);

        Assert.True(column.IsRowVersion);
        Assert.Equal("rowversion", column.ColumnType);
        Assert.False(column.IsNullable);
    }
}
