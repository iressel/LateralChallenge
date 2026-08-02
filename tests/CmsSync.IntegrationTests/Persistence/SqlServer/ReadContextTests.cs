using CmsSync.Infrastructure.Persistence;
using CmsSync.Infrastructure.Persistence.Models;
using CmsSync.IntegrationTests.Infrastructure;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.Persistence.SqlServer;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "SqlServer")]
public sealed class ReadContextTests
{
    private readonly SqlServerFixture _fixture;

    public ReadContextTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProductionReadContextUsesReadLoginAndLeavesQueryUntracked()
    {
        await using var factory = new CmsSyncWebApplicationFactory(
            _fixture.WriteConnectionString,
            _fixture.ReadConnectionString);
        var entityId = $"read-context-{Guid.NewGuid():N}";
        var currentTimestamp = new DateTime(2026, 8, 2, 9, 30, 0, DateTimeKind.Utc);
        var highWatermark = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);

        using (var writeScope = factory.Services.CreateScope())
        {
            var writeContext = writeScope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
            writeContext.CmsEntities.Add(new CmsEntity
            {
                EntityId = entityId,
                Generation = 1,
                LatestVersion = 2,
                Payload = "{\"source\":\"read-context-test\"}",
                PayloadHash = new byte[32],
                CmsPublicationStatus = "Published",
                CurrentVersionOccurredAtUtc = currentTimestamp,
                EntityEventHighWatermarkUtc = highWatermark,
                AdministrativeDisabled = false,
                CreatedAtUtc = currentTimestamp,
                UpdatedAtUtc = highWatermark,
            });
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var readScope = factory.Services.CreateScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<CmsReadDbContext>();
        var entity = await readContext.CmsEntities.SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);

        Assert.Equal(entityId, entity.EntityId);
        Assert.Equal(currentTimestamp, entity.CurrentVersionOccurredAtUtc);
        Assert.Equal(highWatermark, entity.EntityEventHighWatermarkUtc);
        Assert.NotEqual(entity.CurrentVersionOccurredAtUtc, entity.EntityEventHighWatermarkUtc);
        Assert.Equal(QueryTrackingBehavior.NoTracking, readContext.ChangeTracker.QueryTrackingBehavior);
        Assert.Empty(readContext.ChangeTracker.Entries());
        Assert.Equal(
            PersistenceModelConstants.CmsEntitiesTable,
            readContext.Model.FindEntityType(typeof(CmsEntityReadModel))?.GetTableName());
    }

    [Fact]
    public async Task ProductionReadContextRejectsEverySaveChangesOverload()
    {
        await using var factory = new CmsSyncWebApplicationFactory(
            _fixture.WriteConnectionString,
            _fixture.ReadConnectionString);
        using var scope = factory.Services.CreateScope();
        var readContext = scope.ServiceProvider.GetRequiredService<CmsReadDbContext>();

        var synchronous = Assert.Throws<NotSupportedException>(() => readContext.SaveChanges());
        var synchronousBoolean = Assert.Throws<NotSupportedException>(
            () => readContext.SaveChanges(acceptAllChangesOnSuccess: true));
        var asynchronous = await Assert.ThrowsAsync<NotSupportedException>(
            () => readContext.SaveChangesAsync(TestContext.Current.CancellationToken));
        var asynchronousBoolean = await Assert.ThrowsAsync<NotSupportedException>(
            () => readContext.SaveChangesAsync(
                acceptAllChangesOnSuccess: true,
                TestContext.Current.CancellationToken));

        Assert.Equal("CmsReadDbContext is read-only and cannot save changes.", synchronous.Message);
        Assert.Equal(synchronous.Message, synchronousBoolean.Message);
        Assert.Equal(synchronous.Message, asynchronous.Message);
        Assert.Equal(synchronous.Message, asynchronousBoolean.Message);
    }
}
