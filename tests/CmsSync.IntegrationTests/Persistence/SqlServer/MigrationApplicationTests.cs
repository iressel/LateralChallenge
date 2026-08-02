using CmsSync.Infrastructure.Persistence;
using CmsSync.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CmsSync.IntegrationTests.Persistence.SqlServer;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "SqlServer")]
public sealed class MigrationApplicationTests
{
    private readonly SqlServerFixture _fixture;

    public MigrationApplicationTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CleanDatabaseContainsExactlyTheAppliedInitialMigration()
    {
        Assert.Equal(0, _fixture.InitialApplicationTableCount);

        var tables = await SqlServerMetadataReader.ReadApplicationTablesAsync(
            _fixture.WriteConnectionString,
            TestContext.Current.CancellationToken);
        var expectedTables = new[]
        {
            PersistenceModelConstants.CmsDeletionTombstonesTable,
            PersistenceModelConstants.CmsEntitiesTable,
            PersistenceModelConstants.CmsEntityRevisionsTable,
            PersistenceModelConstants.CmsEventProcessingLogsTable,
        };

        Assert.Equal(expectedTables.Order(StringComparer.Ordinal), tables);
        Assert.Equal(4, tables.Length);

        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(_fixture.WriteConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId]";

        var migrationIds = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            migrationIds.Add(reader.GetString(0));
        }

        Assert.Equal(new[] { SqlServerTestConstants.MigrationId }, migrationIds);
    }

    [Fact]
    public async Task ApplyingMigrationAgainIsIdempotentAndReadContextOwnsNone()
    {
        var writeOptions = new DbContextOptionsBuilder<CmsWriteDbContext>()
            .UseSqlServer(_fixture.WriteConnectionString)
            .Options;
        var readOptions = new DbContextOptionsBuilder<CmsReadDbContext>()
            .UseSqlServer(_fixture.ReadConnectionString)
            .Options;

        await using var writeContext = new CmsWriteDbContext(writeOptions);
        await writeContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var appliedMigrations = await writeContext.Database
            .GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        await using var readContext = new CmsReadDbContext(readOptions);
        var readMigrations = readContext.Database.GetMigrations();

        Assert.Equal(new[] { SqlServerTestConstants.MigrationId }, appliedMigrations);
        Assert.Empty(readMigrations);
    }
}
