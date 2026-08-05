using System.Text.RegularExpressions;
using CmsSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace CmsSync.IntegrationTests.Persistence.Migrations;

[Trait("Category", "SqlServer")]
public sealed partial class InitialCmsPersistenceScriptTests
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
    public void NormalMigrationScriptCreatesOnlyTheFourApplicationTables()
    {
        var script = MigrationTestContext.GenerateScript(MigrationsSqlGenerationOptions.Default);
        var applicationTableNames = GetCreatedTableNames(script)
            .Where(tableName => !string.Equals(tableName, "__EFMigrationsHistory", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedTableNames.Order(StringComparer.Ordinal), applicationTableNames);
        Assert.Contains(MigrationId, script, StringComparison.Ordinal);
        AssertCriticalSchemaSql(script);
        AssertNoSensitiveSchemaSql(script);
    }

    [Fact]
    public void IdempotentMigrationScriptContainsHistoryGuardsAndTheSameSchema()
    {
        var script = MigrationTestContext.GenerateScript(MigrationsSqlGenerationOptions.Idempotent);
        var applicationTableNames = GetCreatedTableNames(script)
            .Where(tableName => !string.Equals(tableName, "__EFMigrationsHistory", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedTableNames.Order(StringComparer.Ordinal), applicationTableNames);
        Assert.Contains("IF NOT EXISTS", script, StringComparison.Ordinal);
        Assert.Contains("[__EFMigrationsHistory]", script, StringComparison.Ordinal);
        Assert.Contains(MigrationId, script, StringComparison.Ordinal);
        AssertCriticalSchemaSql(script);
        AssertNoSensitiveSchemaSql(script);
    }

    [Fact]
    public void TombstoneAndProcessingLogScriptDefinitionsArePayloadAndCredentialFree()
    {
        var script = MigrationTestContext.GenerateScript(MigrationsSqlGenerationOptions.Default);
        var tombstoneStatement = GetCreateTableStatement(
            script,
            PersistenceModelConstants.CmsDeletionTombstonesTable);
        var processingLogStatement = GetCreateTableStatement(
            script,
            PersistenceModelConstants.CmsEventProcessingLogsTable);

        Assert.DoesNotContain("[Payload]", tombstoneStatement, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[EventContentHash]", tombstoneStatement, StringComparison.OrdinalIgnoreCase);

        foreach (var prohibitedToken in new[]
                 {
                     "[Payload]",
                     "[RawPayload]",
                     "[RequestBody]",
                     "[Authorization]",
                     "[AuthorizationHeader]",
                     "[Password]",
                     "[Credential]",
                     "[ConnectionString]",
                     "[ExceptionStackTrace]",
                     "[DiagnosticText]",
                 })
        {
            Assert.DoesNotContain(prohibitedToken, tombstoneStatement, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(prohibitedToken, processingLogStatement, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertCriticalSchemaSql(string script)
    {
        foreach (var constraintName in new[]
                 {
                     PersistenceConstraintNames.CmsEntitiesEventTimestamps,
                     PersistenceConstraintNames.CmsEntitiesPayloadJsonObject,
                     PersistenceConstraintNames.CmsEntitiesPublicationStatus,
                     PersistenceConstraintNames.CmsEntityRevisionsPayloadJsonObject,
                     PersistenceConstraintNames.CmsDeletionTombstonesGenerationNonNegative,
                     PersistenceConstraintNames.CmsEventProcessingLogsIdempotencyOwner,
                     PersistenceConstraintNames.CmsEventProcessingLogsReplayDoesNotOwnIdentity,
                     PersistenceConstraintNames.CmsEventProcessingLogsGenerationNonNegative,
                 })
        {
            Assert.Contains(constraintName, script, StringComparison.Ordinal);
        }

        Assert.Contains(PersistenceIndexNames.CmsEventProcessingLogsBatchIdSequence, script, StringComparison.Ordinal);
        Assert.Contains(PersistenceIndexNames.CmsEventProcessingLogsIdempotencyOwner, script, StringComparison.Ordinal);
        Assert.Contains(
            "WHERE [OwnsIdempotencyKey] = CAST(1 AS bit) AND [IdempotencyKey] IS NOT NULL",
            script,
            StringComparison.Ordinal);
        Assert.Contains("[CurrentVersionOccurredAtUtc] datetime2(7) NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("[EntityEventHighWatermarkUtc] datetime2(7) NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("binary(32)", script, StringComparison.Ordinal);
        Assert.Contains("rowversion NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains(PersistenceModelConstants.CaseSensitiveCollation, script, StringComparison.Ordinal);
        Assert.Contains(
            "FOREIGN KEY ([ReplayOfProcessingLogId]) REFERENCES [CmsEventProcessingLogs] ([ProcessingLogId])",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ON DELETE CASCADE", script, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertNoSensitiveSchemaSql(string script)
    {
        foreach (var prohibitedTable in new[]
                 {
                     "CmsEntityReadModel",
                     "CmsIngestionBatches",
                     "CmsProcessedEvents",
                     "CmsEventAttempts",
                     "OutboxMessages",
                     "AuditLogs",
                 })
        {
            Assert.DoesNotContain($"CREATE TABLE [{prohibitedTable}]", script, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var sensitiveToken in new[]
                 {
                     "Password=",
                     "User ID=",
                     "Authorization:",
                     "Server=",
                     "Data Source=",
                     "TrustServerCertificate=",
                     "StackTrace",
                 })
        {
            Assert.DoesNotContain(sensitiveToken, script, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string[] GetCreatedTableNames(string script)
    {
        return CreateTableRegex()
            .Matches(script)
            .Select(match => match.Groups["table"].Value)
            .ToArray();
    }

    private static string GetCreateTableStatement(string script, string tableName)
    {
        var pattern = $"CREATE TABLE \\[{Regex.Escape(tableName)}\\]\\s*\\((?<body>.*?)\\r?\\n\\);";
        var match = Regex.Match(
            script,
            pattern,
            RegexOptions.CultureInvariant | RegexOptions.Singleline);

        Assert.True(match.Success, $"The script does not contain a complete CREATE TABLE for {tableName}.");
        return match.Value;
    }

    [GeneratedRegex("CREATE TABLE \\[(?<table>[^\\]]+)\\]", RegexOptions.CultureInvariant)]
    private static partial Regex CreateTableRegex();
}
