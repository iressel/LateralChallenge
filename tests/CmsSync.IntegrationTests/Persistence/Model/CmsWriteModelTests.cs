using CmsSync.Infrastructure.Persistence;
using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CmsSync.IntegrationTests.Persistence.Model;

[Trait("Category", "SqlServer")]
public sealed class CmsWriteModelTests
{
    [Fact]
    public void ModelContainsExactlyTheFourRequiredTables()
    {
        using var context = PersistenceModelTestContext.CreateWriteContext();
        var model = PersistenceModelTestContext.GetDesignTimeModel(context);

        var tableNames = model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(tableName => tableName is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                PersistenceModelConstants.CmsDeletionTombstonesTable,
                PersistenceModelConstants.CmsEntitiesTable,
                PersistenceModelConstants.CmsEntityRevisionsTable,
                PersistenceModelConstants.CmsEventProcessingLogsTable,
            },
            tableNames);
    }

    [Fact]
    public void CmsEntitiesMapsCurrentStateAndDistinctEventTimestamps()
    {
        using var context = PersistenceModelTestContext.CreateWriteContext();
        var model = PersistenceModelTestContext.GetDesignTimeModel(context);
        var entityType = PersistenceModelTestContext.GetRequiredEntityType<CmsEntity>(model);
        var primaryKey = Assert.Single(entityType.GetKeys());

        Assert.Equal(new[] { nameof(CmsEntity.EntityId) }, PropertyNames(primaryKey.Properties));
        AssertIdentifier(entityType, nameof(CmsEntity.EntityId), PersistenceModelConstants.EntityIdentifierMaxLength);
        AssertColumnType(
            entityType,
            nameof(CmsEntity.Generation),
            PersistenceModelConstants.BigIntColumnType,
            nullable: false);
        AssertColumnType(
            entityType,
            nameof(CmsEntity.LatestVersion),
            PersistenceModelConstants.BigIntColumnType,
            nullable: false);
        AssertColumnType(
            entityType,
            nameof(CmsEntity.Payload),
            PersistenceModelConstants.PayloadColumnType,
            nullable: false);
        AssertHash(entityType, nameof(CmsEntity.PayloadHash), nullable: false);

        var publicationStatus = PersistenceModelTestContext.GetRequiredProperty(
            entityType,
            nameof(CmsEntity.CmsPublicationStatus));
        Assert.False(publicationStatus.IsNullable);
        Assert.Equal(PersistenceModelConstants.PublicationStatusMaxLength, publicationStatus.GetMaxLength());
        Assert.Equal(PersistenceModelConstants.CaseSensitiveCollation, publicationStatus.GetCollation());

        AssertDateTime(entityType, nameof(CmsEntity.CurrentVersionOccurredAtUtc), nullable: false);
        AssertDateTime(entityType, nameof(CmsEntity.EntityEventHighWatermarkUtc), nullable: false);
        AssertIdentifier(
            entityType,
            nameof(CmsEntity.AdministrativeStateChangedBy),
            PersistenceModelConstants.AdministrativeSubjectMaxLength);

        var table = StoreObjectIdentifier.Table(
            PersistenceModelConstants.CmsEntitiesTable,
            entityType.GetSchema());
        var currentColumn = PersistenceModelTestContext.GetRequiredProperty(
            entityType,
            nameof(CmsEntity.CurrentVersionOccurredAtUtc)).GetColumnName(table);
        var watermarkColumn = PersistenceModelTestContext.GetRequiredProperty(
            entityType,
            nameof(CmsEntity.EntityEventHighWatermarkUtc)).GetColumnName(table);

        Assert.Equal(nameof(CmsEntity.CurrentVersionOccurredAtUtc), currentColumn);
        Assert.Equal(nameof(CmsEntity.EntityEventHighWatermarkUtc), watermarkColumn);
        Assert.NotEqual(currentColumn, watermarkColumn);
        AssertRowVersion(entityType, nameof(CmsEntity.RowVersion));

        var checks = CheckConstraintSql(entityType);
        Assert.Contains("[Generation] > 0", checks, StringComparer.Ordinal);
        Assert.Contains("[LatestVersion] > 0", checks, StringComparer.Ordinal);
        Assert.Contains("ISJSON([Payload], OBJECT) = 1", checks, StringComparer.Ordinal);
        Assert.Contains(
            "[CmsPublicationStatus] IN ('Published', 'Unpublished')",
            checks,
            StringComparer.Ordinal);
        Assert.Contains(
            "[EntityEventHighWatermarkUtc] >= [CurrentVersionOccurredAtUtc]",
            checks,
            StringComparer.Ordinal);
        Assert.Contains(checks, sql =>
            sql.Contains(nameof(CmsEntity.AdministrativeStateChangedAtUtc), StringComparison.Ordinal) &&
            sql.Contains(nameof(CmsEntity.AdministrativeStateChangedBy), StringComparison.Ordinal));
        AssertCheckConstraintNames(
            entityType,
            PersistenceConstraintNames.CmsEntitiesGenerationPositive,
            PersistenceConstraintNames.CmsEntitiesLatestVersionPositive,
            PersistenceConstraintNames.CmsEntitiesPayloadJsonObject,
            PersistenceConstraintNames.CmsEntitiesPublicationStatus,
            PersistenceConstraintNames.CmsEntitiesEventTimestamps,
            PersistenceConstraintNames.CmsEntitiesAdministrativeAudit);
    }

    [Fact]
    public void CmsEntityRevisionsHaveImmutableCompositeIdentityAndPayload()
    {
        using var context = PersistenceModelTestContext.CreateWriteContext();
        var model = PersistenceModelTestContext.GetDesignTimeModel(context);
        var entityType = PersistenceModelTestContext.GetRequiredEntityType<CmsEntityRevision>(model);
        var primaryKey = Assert.Single(entityType.GetKeys());

        Assert.Equal(
            new[]
            {
                nameof(CmsEntityRevision.EntityId),
                nameof(CmsEntityRevision.Generation),
                nameof(CmsEntityRevision.Version),
            },
            PropertyNames(primaryKey.Properties));
        AssertIdentifier(
            entityType,
            nameof(CmsEntityRevision.EntityId),
            PersistenceModelConstants.EntityIdentifierMaxLength);
        AssertColumnType(
            entityType,
            nameof(CmsEntityRevision.Generation),
            PersistenceModelConstants.BigIntColumnType,
            nullable: false);
        AssertColumnType(
            entityType,
            nameof(CmsEntityRevision.Version),
            PersistenceModelConstants.BigIntColumnType,
            nullable: false);
        AssertColumnType(
            entityType,
            nameof(CmsEntityRevision.FirstObservedPayload),
            PersistenceModelConstants.PayloadColumnType,
            nullable: false);
        AssertHash(entityType, nameof(CmsEntityRevision.PayloadHash), nullable: false);
        AssertDateTime(entityType, nameof(CmsEntityRevision.FirstObservedAtUtc), nullable: false);

        AssertImmutable(entityType, nameof(CmsEntityRevision.FirstObservedPayload));
        AssertImmutable(entityType, nameof(CmsEntityRevision.PayloadHash));
        AssertImmutable(entityType, nameof(CmsEntityRevision.FirstObservedAtUtc));

        var checks = CheckConstraintSql(entityType);
        Assert.Contains("[Generation] > 0", checks, StringComparer.Ordinal);
        Assert.Contains("[Version] > 0", checks, StringComparer.Ordinal);
        Assert.Contains("ISJSON([FirstObservedPayload], OBJECT) = 1", checks, StringComparer.Ordinal);
        AssertCheckConstraintNames(
            entityType,
            PersistenceConstraintNames.CmsEntityRevisionsGenerationPositive,
            PersistenceConstraintNames.CmsEntityRevisionsVersionPositive,
            PersistenceConstraintNames.CmsEntityRevisionsPayloadJsonObject);
        Assert.DoesNotContain(nameof(CmsEntity.CmsPublicationStatus), PropertyNames(entityType.GetProperties()));
    }

    [Fact]
    public void TombstoneIsPayloadFreeAndAllowsGenerationZero()
    {
        using var context = PersistenceModelTestContext.CreateWriteContext();
        var model = PersistenceModelTestContext.GetDesignTimeModel(context);
        var entityType = PersistenceModelTestContext.GetRequiredEntityType<CmsDeletionTombstone>(model);

        AssertIdentifier(
            entityType,
            nameof(CmsDeletionTombstone.EntityId),
            PersistenceModelConstants.EntityIdentifierMaxLength);
        AssertColumnType(
            entityType,
            nameof(CmsDeletionTombstone.LastDeletedGeneration),
            PersistenceModelConstants.BigIntColumnType,
            nullable: false);
        AssertDateTime(entityType, nameof(CmsDeletionTombstone.DeletedAtUtc), nullable: false);
        AssertDateTime(entityType, nameof(CmsDeletionTombstone.CreatedAtUtc), nullable: false);
        AssertDateTime(entityType, nameof(CmsDeletionTombstone.UpdatedAtUtc), nullable: false);
        AssertRowVersion(entityType, nameof(CmsDeletionTombstone.RowVersion));

        var lastDeleteKey = PersistenceModelTestContext.GetRequiredProperty(
            entityType,
            nameof(CmsDeletionTombstone.LastDeleteEventKey));
        Assert.Equal(PersistenceModelConstants.IdempotencyKeyMaxLength, lastDeleteKey.GetMaxLength());
        Assert.Equal(PersistenceModelConstants.CaseSensitiveCollation, lastDeleteKey.GetCollation());
        Assert.Contains(
            "[LastDeletedGeneration] >= 0",
            CheckConstraintSql(entityType),
            StringComparer.Ordinal);
        AssertCheckConstraintNames(
            entityType,
            PersistenceConstraintNames.CmsDeletionTombstonesGenerationNonNegative);

        AssertExactProperties(
            entityType,
            nameof(CmsDeletionTombstone.EntityId),
            nameof(CmsDeletionTombstone.LastDeletedGeneration),
            nameof(CmsDeletionTombstone.DeletedAtUtc),
            nameof(CmsDeletionTombstone.LastDeleteEventKey),
            nameof(CmsDeletionTombstone.CreatedAtUtc),
            nameof(CmsDeletionTombstone.UpdatedAtUtc),
            nameof(CmsDeletionTombstone.RowVersion));
    }

    [Fact]
    public void ProcessingLogEnforcesAttemptIdentityAndReplayInvariants()
    {
        using var context = PersistenceModelTestContext.CreateWriteContext();
        var model = PersistenceModelTestContext.GetDesignTimeModel(context);
        var entityType = PersistenceModelTestContext.GetRequiredEntityType<CmsEventProcessingLog>(model);
        var primaryKey = Assert.Single(entityType.GetKeys());

        Assert.Equal(
            new[] { nameof(CmsEventProcessingLog.ProcessingLogId) },
            PropertyNames(primaryKey.Properties));

        var batchIndex = entityType.GetIndexes().Single(index =>
            PropertyNames(index.Properties).SequenceEqual(
                new[]
                {
                    nameof(CmsEventProcessingLog.BatchId),
                    nameof(CmsEventProcessingLog.Sequence),
                },
                StringComparer.Ordinal));
        Assert.True(batchIndex.IsUnique);
        Assert.Equal(
            PersistenceIndexNames.CmsEventProcessingLogsBatchIdSequence,
            batchIndex.GetDatabaseName());

        var ownerIndex = entityType.GetIndexes().Single(index =>
            PropertyNames(index.Properties).SequenceEqual(
                new[] { nameof(CmsEventProcessingLog.IdempotencyKey) },
                StringComparer.Ordinal));
        Assert.True(ownerIndex.IsUnique);
        Assert.Equal(
            PersistenceIndexNames.CmsEventProcessingLogsIdempotencyOwner,
            ownerIndex.GetDatabaseName());
        Assert.Equal(
            "[OwnsIdempotencyKey] = CAST(1 AS bit) AND [IdempotencyKey] IS NOT NULL",
            ownerIndex.GetFilter());

        var replayForeignKey = Assert.Single(entityType.GetForeignKeys());
        Assert.Equal(
            new[] { nameof(CmsEventProcessingLog.ReplayOfProcessingLogId) },
            PropertyNames(replayForeignKey.Properties));
        Assert.Same(entityType, replayForeignKey.PrincipalEntityType);
        Assert.Equal(DeleteBehavior.NoAction, replayForeignKey.DeleteBehavior);

        AssertIdentifier(
            entityType,
            nameof(CmsEventProcessingLog.IdempotencyKey),
            PersistenceModelConstants.IdempotencyKeyMaxLength);
        AssertIdentifier(
            entityType,
            nameof(CmsEventProcessingLog.ExternalEventId),
            PersistenceModelConstants.ExternalEventIdentifierMaxLength);
        AssertIdentifier(
            entityType,
            nameof(CmsEventProcessingLog.EntityId),
            PersistenceModelConstants.EntityIdentifierMaxLength);
        AssertIdentifier(
            entityType,
            nameof(CmsEventProcessingLog.CorrelationId),
            PersistenceModelConstants.CorrelationIdentifierMaxLength);
        AssertIdentifier(
            entityType,
            nameof(CmsEventProcessingLog.AuthenticatedCmsSubject),
            PersistenceModelConstants.CmsSubjectIdentifierMaxLength);
        AssertHash(entityType, nameof(CmsEventProcessingLog.EventContentHash), nullable: true);
        AssertHash(entityType, nameof(CmsEventProcessingLog.PayloadHash), nullable: true);
        AssertDateTime(entityType, nameof(CmsEventProcessingLog.EventOccurredAtUtc), nullable: true);
        AssertDateTime(entityType, nameof(CmsEventProcessingLog.ProcessedAtUtc), nullable: false);
        AssertColumnType(
            entityType,
            nameof(CmsEventProcessingLog.Version),
            PersistenceModelConstants.BigIntColumnType,
            nullable: true);
        AssertColumnType(
            entityType,
            nameof(CmsEventProcessingLog.Generation),
            PersistenceModelConstants.BigIntColumnType,
            nullable: true);
        AssertColumnType(
            entityType,
            nameof(CmsEventProcessingLog.ResultingVersion),
            PersistenceModelConstants.BigIntColumnType,
            nullable: true);

        foreach (var categoricalPropertyName in new[]
                 {
                     nameof(CmsEventProcessingLog.EventType),
                     nameof(CmsEventProcessingLog.Outcome),
                     nameof(CmsEventProcessingLog.Code),
                 })
        {
            var categoricalProperty = PersistenceModelTestContext.GetRequiredProperty(
                entityType,
                categoricalPropertyName);
            Assert.Equal(PersistenceModelConstants.CaseSensitiveCollation, categoricalProperty.GetCollation());
        }

        var generationConstraint = Assert.Single(
            entityType.GetCheckConstraints(),
            constraint => string.Equals(
                constraint.Name,
                PersistenceConstraintNames.CmsEventProcessingLogsGenerationNonNegative,
                StringComparison.Ordinal));
        Assert.Equal("[Generation] IS NULL OR [Generation] >= 0", generationConstraint.Sql);
        Assert.DoesNotContain(
            entityType.GetCheckConstraints(),
            constraint => string.Equals(
                constraint.Name,
                "CK_CmsEventProcessingLogs_Generation_Positive",
                StringComparison.Ordinal));

        var checks = CheckConstraintSql(entityType);
        Assert.Contains("[Sequence] >= 0", checks, StringComparer.Ordinal);
        Assert.Contains("[Generation] IS NULL OR [Generation] >= 0", checks, StringComparer.Ordinal);
        Assert.Contains(
            "[Outcome] IN ('Applied', 'Duplicate', 'Equivalent', 'Stale', 'Invalid', 'Conflict')",
            checks,
            StringComparer.Ordinal);
        Assert.Contains(
            "[EventType] IS NULL OR [EventType] IN ('publish', 'unpublish', 'delete')",
            checks,
            StringComparer.Ordinal);
        AssertCheckConstraintNames(
            entityType,
            PersistenceConstraintNames.CmsEventProcessingLogsSequenceNonNegative,
            PersistenceConstraintNames.CmsEventProcessingLogsIdempotencyOwner,
            PersistenceConstraintNames.CmsEventProcessingLogsReplayDoesNotOwnIdentity,
            PersistenceConstraintNames.CmsEventProcessingLogsEventType,
            PersistenceConstraintNames.CmsEventProcessingLogsOutcome,
            PersistenceConstraintNames.CmsEventProcessingLogsVersionPositive,
            PersistenceConstraintNames.CmsEventProcessingLogsGenerationNonNegative,
            PersistenceConstraintNames.CmsEventProcessingLogsResultingVersionPositive);

        AssertExactProperties(
            entityType,
            nameof(CmsEventProcessingLog.ProcessingLogId),
            nameof(CmsEventProcessingLog.BatchId),
            nameof(CmsEventProcessingLog.Sequence),
            nameof(CmsEventProcessingLog.IdempotencyKey),
            nameof(CmsEventProcessingLog.OwnsIdempotencyKey),
            nameof(CmsEventProcessingLog.ReplayOfProcessingLogId),
            nameof(CmsEventProcessingLog.ExternalEventId),
            nameof(CmsEventProcessingLog.EventContentHash),
            nameof(CmsEventProcessingLog.PayloadHash),
            nameof(CmsEventProcessingLog.EventType),
            nameof(CmsEventProcessingLog.EntityId),
            nameof(CmsEventProcessingLog.Version),
            nameof(CmsEventProcessingLog.EventOccurredAtUtc),
            nameof(CmsEventProcessingLog.Outcome),
            nameof(CmsEventProcessingLog.Code),
            nameof(CmsEventProcessingLog.Generation),
            nameof(CmsEventProcessingLog.ResultingVersion),
            nameof(CmsEventProcessingLog.ProcessedAtUtc),
            nameof(CmsEventProcessingLog.CorrelationId),
            nameof(CmsEventProcessingLog.AuthenticatedCmsSubject));
    }

    [Fact]
    public void NoRelationshipCanCascadeDeleteLogsOrTombstones()
    {
        using var context = PersistenceModelTestContext.CreateWriteContext();
        var model = PersistenceModelTestContext.GetDesignTimeModel(context);
        var protectedTypes = new[]
        {
            typeof(CmsDeletionTombstone),
            typeof(CmsEventProcessingLog),
        };
        var incomingForeignKeys = model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Where(foreignKey => protectedTypes.Contains(foreignKey.PrincipalEntityType.ClrType))
            .ToArray();

        Assert.All(incomingForeignKeys, foreignKey => Assert.NotEqual(DeleteBehavior.Cascade, foreignKey.DeleteBehavior));
    }

    [Fact]
    public void EveryMappedStringIsBoundedUnlessItIsAnOpaqueJsonPayload()
    {
        using var context = PersistenceModelTestContext.CreateWriteContext();
        var model = PersistenceModelTestContext.GetDesignTimeModel(context);
        var payloadProperties = new[]
        {
            $"{nameof(CmsEntity)}.{nameof(CmsEntity.Payload)}",
            $"{nameof(CmsEntityRevision)}.{nameof(CmsEntityRevision.FirstObservedPayload)}",
        };

        foreach (var property in model.GetEntityTypes()
                     .SelectMany(entityType => entityType.GetProperties())
                     .Where(property => property.ClrType == typeof(string)))
        {
            var qualifiedName = $"{property.DeclaringType.ClrType.Name}.{property.Name}";

            if (payloadProperties.Contains(qualifiedName, StringComparer.Ordinal))
            {
                Assert.Equal(PersistenceModelConstants.PayloadColumnType, property.GetColumnType());
                continue;
            }

            Assert.NotNull(property.GetMaxLength());
            Assert.InRange(property.GetMaxLength()!.Value, 1, 450);
        }
    }

    private static void AssertIdentifier(IEntityType entityType, string propertyName, int maximumLength)
    {
        var property = PersistenceModelTestContext.GetRequiredProperty(entityType, propertyName);

        Assert.Equal(maximumLength, property.GetMaxLength());
        Assert.Equal(PersistenceModelConstants.CaseSensitiveCollation, property.GetCollation());
    }

    private static void AssertColumnType(
        IEntityType entityType,
        string propertyName,
        string columnType,
        bool nullable)
    {
        var property = PersistenceModelTestContext.GetRequiredProperty(entityType, propertyName);

        Assert.Equal(columnType, property.GetColumnType());
        Assert.Equal(nullable, property.IsNullable);
    }

    private static void AssertDateTime(IEntityType entityType, string propertyName, bool nullable)
    {
        var property = PersistenceModelTestContext.GetRequiredProperty(entityType, propertyName);

        Assert.Equal(PersistenceModelConstants.DateTimeColumnType, property.GetColumnType());
        Assert.Equal(PersistenceModelConstants.DateTimePrecision, property.GetPrecision());
        Assert.Equal(nullable, property.IsNullable);
    }

    private static void AssertHash(IEntityType entityType, string propertyName, bool nullable)
    {
        var property = PersistenceModelTestContext.GetRequiredProperty(entityType, propertyName);

        Assert.Equal(PersistenceModelConstants.HashColumnType, property.GetColumnType());
        Assert.Equal(PersistenceModelConstants.HashLength, property.GetMaxLength());
        Assert.True(property.IsFixedLength());
        Assert.Equal(nullable, property.IsNullable);
    }

    private static void AssertRowVersion(IEntityType entityType, string propertyName)
    {
        var property = PersistenceModelTestContext.GetRequiredProperty(entityType, propertyName);

        Assert.True(property.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        Assert.Equal("rowversion", property.GetColumnType());
        Assert.False(property.IsNullable);
    }

    private static void AssertImmutable(IEntityType entityType, string propertyName)
    {
        var property = PersistenceModelTestContext.GetRequiredProperty(entityType, propertyName);

        Assert.Equal(PropertySaveBehavior.Throw, property.GetAfterSaveBehavior());
    }

    private static string[] CheckConstraintSql(IEntityType entityType)
    {
        return entityType.GetCheckConstraints()
            .Select(constraint => constraint.Sql)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static void AssertCheckConstraintNames(
        IEntityType entityType,
        params string[] expectedNames)
    {
        var actualNames = entityType.GetCheckConstraints()
            .Select(constraint => constraint.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedNames.Order(StringComparer.Ordinal), actualNames);
    }

    private static string[] PropertyNames(IEnumerable<IReadOnlyProperty> properties)
    {
        return properties.Select(property => property.Name).ToArray();
    }

    private static void AssertExactProperties(IEntityType entityType, params string[] expectedProperties)
    {
        var actualProperties = PropertyNames(entityType.GetProperties())
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedProperties.Order(StringComparer.Ordinal), actualProperties);
    }
}
