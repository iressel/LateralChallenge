using CmsSync.Application.EventIngestion;
using CmsSync.Domain.Processing;
using CmsSync.Infrastructure.Persistence;
using CmsSync.Infrastructure.Persistence.Models;
using CmsSync.IntegrationTests.Infrastructure;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.EventIngestion;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "EventProcessing")]
public sealed class DeleteRecreationProcessingTests
{
    private readonly SqlServerFixture _fixture;

    public DeleteRecreationProcessingTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TombstoneOnlyDeleteMatrixCreatesAdvancesAndRejectsOlderWatermarks()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("tombstone-matrix");

        var created = await ProcessSingleAsync(factory, EventProcessingTestData.Delete(
            entityId,
            EventProcessingTestData.UniqueId("event"),
            "2026-08-02T10:00:00Z"));
        var stale = await ProcessSingleAsync(factory, EventProcessingTestData.Delete(
            entityId,
            EventProcessingTestData.UniqueId("event"),
            "2026-08-02T09:00:00Z"));
        var equivalent = await ProcessSingleAsync(factory, EventProcessingTestData.Delete(
            entityId,
            EventProcessingTestData.UniqueId("event"),
            "2026-08-02T10:00:00Z"));
        var advanced = await ProcessSingleAsync(factory, EventProcessingTestData.Delete(
            entityId,
            EventProcessingTestData.UniqueId("event"),
            "2026-08-02T11:00:00Z"));

        Assert.Equal(ProcessingOutcome.Applied, created.Outcome);
        Assert.Equal("TOMBSTONE_CREATED", created.Code);
        Assert.Equal(ProcessingOutcome.Stale, stale.Outcome);
        Assert.Equal(ProcessingOutcome.Equivalent, equivalent.Outcome);
        Assert.Equal(ProcessingOutcome.Applied, advanced.Outcome);
        var tombstone = await ReadTombstoneAsync(factory, entityId);
        Assert.Equal(0, tombstone.LastDeletedGeneration);
        Assert.Equal(new DateTime(2026, 8, 2, 11, 0, 0, DateTimeKind.Utc), tombstone.DeletedAtUtc);
    }

    [Fact]
    public async Task LaterDeleteRemovesActiveStateAndEveryRevisionButRetainsLogsAndTombstone()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("active-delete");
        var firstEventId = EventProcessingTestData.UniqueId("event");
        var secondEventId = EventProcessingTestData.UniqueId("event");
        var deleteEventId = EventProcessingTestData.UniqueId("delete");

        _ = await ProcessSingleAsync(factory, EventProcessingTestData.Publish(
            entityId,
            version: 1,
            eventId: firstEventId,
            timestamp: "2026-08-02T09:00:00Z"));
        _ = await ProcessSingleAsync(factory, EventProcessingTestData.Publish(
            entityId,
            version: 3,
            eventId: secondEventId,
            timestamp: "2026-08-02T10:00:00Z",
            payload: "{\"value\":3}"));
        var deleted = await ProcessSingleAsync(factory, EventProcessingTestData.Delete(
            entityId,
            deleteEventId,
            "2026-08-02T11:00:00Z"));

        Assert.Equal(ProcessingOutcome.Applied, deleted.Outcome);
        Assert.Equal("ENTITY_DELETED", deleted.Code);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        Assert.False(await context.CmsEntities.AnyAsync(
            entity => entity.EntityId == entityId,
            TestContext.Current.CancellationToken));
        Assert.False(await context.CmsEntityRevisions.AnyAsync(
            revision => revision.EntityId == entityId,
            TestContext.Current.CancellationToken));
        var tombstone = await context.CmsDeletionTombstones.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        var logs = await context.CmsEventProcessingLogs.AsNoTracking()
            .Where(log => log.EntityId == entityId)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, tombstone.LastDeletedGeneration);
        Assert.Equal($"external:{deleteEventId}", tombstone.LastDeleteEventKey);
        Assert.Equal(3, logs.Length);
        Assert.All(logs, log => Assert.NotNull(log.EventContentHash));
    }

    [Theory]
    [InlineData("publish", "Published")]
    [InlineData("unpublish", "Unpublished")]
    public async Task LaterVersionedEventRecreatesNextGenerationWithAnyPositiveVersion(
        string eventType,
        string expectedStatus)
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("recreation");

        _ = await ProcessSingleAsync(factory, EventProcessingTestData.Delete(
            entityId,
            EventProcessingTestData.UniqueId("delete"),
            "2026-08-02T10:00:00Z"));
        var stale = await ProcessSingleAsync(factory, EventProcessingTestData.Publish(
            entityId,
            version: 99,
            eventId: EventProcessingTestData.UniqueId("stale"),
            timestamp: "2026-08-02T10:00:00Z",
            type: eventType));
        var recreated = await ProcessSingleAsync(factory, EventProcessingTestData.Publish(
            entityId,
            version: 7,
            eventId: EventProcessingTestData.UniqueId("recreate"),
            timestamp: "2026-08-02T11:00:00Z",
            payload: "{\"recreated\":true}",
            type: eventType));

        Assert.Equal(ProcessingOutcome.Stale, stale.Outcome);
        Assert.Equal(ProcessingOutcome.Applied, recreated.Outcome);
        Assert.Equal("ENTITY_RECREATED", recreated.Code);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var entity = await context.CmsEntities.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        var tombstone = await context.CmsDeletionTombstones.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, entity.Generation);
        Assert.Equal(7, entity.LatestVersion);
        Assert.Equal(expectedStatus, entity.CmsPublicationStatus);
        Assert.False(entity.AdministrativeDisabled);
        Assert.Equal(new DateTime(2026, 8, 2, 11, 0, 0, DateTimeKind.Utc), entity.CurrentVersionOccurredAtUtc);
        Assert.Equal(entity.CurrentVersionOccurredAtUtc, entity.EntityEventHighWatermarkUtc);
        Assert.Equal(0, tombstone.LastDeletedGeneration);
    }

    [Fact]
    public async Task RepeatedDeleteAndRecreationIncrementGenerationWithoutLosingTombstone()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("generation-cycle");

        _ = await ProcessSingleAsync(factory, EventProcessingTestData.Publish(
            entityId,
            eventId: EventProcessingTestData.UniqueId("event"),
            timestamp: "2026-08-02T08:00:00Z"));
        _ = await ProcessSingleAsync(factory, EventProcessingTestData.Delete(
            entityId,
            EventProcessingTestData.UniqueId("delete"),
            "2026-08-02T09:00:00Z"));
        _ = await ProcessSingleAsync(factory, EventProcessingTestData.Publish(
            entityId,
            version: 8,
            eventId: EventProcessingTestData.UniqueId("event"),
            timestamp: "2026-08-02T10:00:00Z"));
        _ = await ProcessSingleAsync(factory, EventProcessingTestData.Delete(
            entityId,
            EventProcessingTestData.UniqueId("delete"),
            "2026-08-02T11:00:00Z"));
        _ = await ProcessSingleAsync(factory, EventProcessingTestData.Publish(
            entityId,
            version: 3,
            eventId: EventProcessingTestData.UniqueId("event"),
            timestamp: "2026-08-02T12:00:00Z"));

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var entity = await context.CmsEntities.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        var tombstone = await context.CmsDeletionTombstones.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);

        Assert.Equal(3, entity.Generation);
        Assert.Equal(3, entity.LatestVersion);
        Assert.Equal(2, tombstone.LastDeletedGeneration);
        Assert.Equal(new DateTime(2026, 8, 2, 11, 0, 0, DateTimeKind.Utc), tombstone.DeletedAtUtc);
    }

    [Fact]
    public async Task GenerationExhaustionIsAConflictWithoutOverflowOrStateMutation()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("generation-exhaustion");
        await InsertMaximumGenerationTombstoneAsync(factory, entityId);

        var conflict = await ProcessSingleAsync(factory, EventProcessingTestData.Publish(
            entityId,
            version: long.MaxValue,
            eventId: EventProcessingTestData.UniqueId("event"),
            timestamp: "2026-08-02T11:00:00Z"));

        Assert.Equal(ProcessingOutcome.Conflict, conflict.Outcome);
        Assert.Equal("GENERATION_EXHAUSTED", conflict.Code);
        Assert.False(await EntityExistsAsync(factory, entityId));
        Assert.Equal(long.MaxValue, (await ReadTombstoneAsync(factory, entityId)).LastDeletedGeneration);
    }

    [Fact]
    public async Task AC053AndAC057UseHighWatermarkForEveryDeleteBoundaryAndPersistEveryResult()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("ac057");
        var batchId = Guid.NewGuid();
        var events = new[]
        {
            EventProcessingTestData.Publish(
                entityId,
                version: 5,
                eventId: EventProcessingTestData.UniqueId("event"),
                timestamp: "2026-08-02T10:00:00Z",
                payload: "{\"version\":5}"),
            EventProcessingTestData.Publish(
                entityId,
                version: 6,
                eventId: EventProcessingTestData.UniqueId("event"),
                timestamp: "2026-08-02T09:00:00Z",
                payload: "{\"version\":6}"),
            EventProcessingTestData.Delete(
                entityId,
                EventProcessingTestData.UniqueId("delete"),
                "2026-08-02T09:30:00Z"),
            EventProcessingTestData.Delete(
                entityId,
                EventProcessingTestData.UniqueId("delete"),
                "2026-08-02T10:00:00Z"),
            EventProcessingTestData.Delete(
                entityId,
                EventProcessingTestData.UniqueId("delete"),
                "2026-08-02T10:00:00.0000001Z"),
        };

        var result = await EventProcessingTestData.ProcessAsync(
            factory.Services,
            events,
            batchId,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            new[]
            {
                ProcessingOutcome.Applied,
                ProcessingOutcome.Applied,
                ProcessingOutcome.Stale,
                ProcessingOutcome.Conflict,
                ProcessingOutcome.Applied,
            },
            result.Results.Select(item => item.Outcome));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        Assert.False(await context.CmsEntities.AnyAsync(
            entity => entity.EntityId == entityId,
            TestContext.Current.CancellationToken));
        Assert.False(await context.CmsEntityRevisions.AnyAsync(
            revision => revision.EntityId == entityId,
            TestContext.Current.CancellationToken));
        var tombstone = await context.CmsDeletionTombstones.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        var logs = await context.CmsEventProcessingLogs.AsNoTracking()
            .Where(log => log.BatchId == batchId)
            .OrderBy(log => log.Sequence)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc).AddTicks(1), tombstone.DeletedAtUtc);
        Assert.Equal(5, logs.Length);
        Assert.Equal("DELETE_STALE", logs[2].Code);
        Assert.Equal("DELETE_CONFLICT", logs[3].Code);
        Assert.Equal("ENTITY_DELETED", logs[4].Code);
    }

    private static async Task<CmsEventBatchItemResult> ProcessSingleAsync(
        CmsSyncWebApplicationFactory factory,
        string eventJson)
    {
        var result = await EventProcessingTestData.ProcessAsync(
            factory.Services,
            [eventJson],
            cancellationToken: TestContext.Current.CancellationToken);
        return Assert.Single(result.Results);
    }

    private static async Task<CmsDeletionTombstone> ReadTombstoneAsync(
        CmsSyncWebApplicationFactory factory,
        string entityId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsDeletionTombstones.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
    }

    private static async Task InsertMaximumGenerationTombstoneAsync(
        CmsSyncWebApplicationFactory factory,
        string entityId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var timestamp = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        context.CmsDeletionTombstones.Add(new CmsDeletionTombstone
        {
            EntityId = entityId,
            LastDeletedGeneration = long.MaxValue,
            DeletedAtUtc = timestamp,
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<bool> EntityExistsAsync(
        CmsSyncWebApplicationFactory factory,
        string entityId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEntities.AnyAsync(
            entity => entity.EntityId == entityId,
            TestContext.Current.CancellationToken);
    }

    private CmsSyncWebApplicationFactory CreateFactory()
    {
        return new CmsSyncWebApplicationFactory(
            _fixture.WriteConnectionString,
            _fixture.ReadConnectionString);
    }
}
