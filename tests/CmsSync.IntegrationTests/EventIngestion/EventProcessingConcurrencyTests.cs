using CmsSync.Application.EventIngestion;
using CmsSync.Domain.Processing;
using CmsSync.Infrastructure.Persistence;
using CmsSync.IntegrationTests.Infrastructure;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.EventIngestion;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "EventProcessing")]
public sealed class EventProcessingConcurrencyTests
{
    private static readonly ProcessingOutcome[] AppliedAndDuplicate =
    [
        ProcessingOutcome.Applied,
        ProcessingOutcome.Duplicate,
    ];

    private static readonly ProcessingOutcome[] AppliedAndConflict =
    [
        ProcessingOutcome.Applied,
        ProcessingOutcome.Conflict,
    ];

    private readonly SqlServerFixture _fixture;

    public EventProcessingConcurrencyTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentFirstEventsForAbsentEntityProduceOneSerialState()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("concurrent-first");
        var firstEvent = EventProcessingTestData.Publish(
            entityId,
            version: 1,
            eventId: EventProcessingTestData.UniqueId("event"));
        var secondEvent = EventProcessingTestData.Publish(
            entityId,
            version: 2,
            eventId: EventProcessingTestData.UniqueId("event"),
            timestamp: "2026-08-02T11:00:00Z",
            payload: "{\"value\":2}");

        var results = await ProcessConcurrentlyAsync(factory.Services, firstEvent, secondEvent);

        Assert.All(results, result => Assert.True(
            result.Results[0].Outcome is ProcessingOutcome.Applied or ProcessingOutcome.Stale));
        Assert.Contains(results, result => result.Results[0].Outcome == ProcessingOutcome.Applied);

        await using var context = CreateContext();
        var entity = await context.CmsEntities.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        Assert.Equal(2, entity.LatestVersion);
        Assert.Equal(2, await context.CmsEventProcessingLogs.CountAsync(
            log => log.EntityId == entityId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentExactExternalIdentityProducesOneOwnerAndOneReplay()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("concurrent-exact");
        var eventId = EventProcessingTestData.UniqueId("event");
        var eventJson = EventProcessingTestData.Publish(entityId, eventId: eventId);

        var results = await ProcessConcurrentlyAsync(factory.Services, eventJson, eventJson);

        Assert.Equal(
            AppliedAndDuplicate,
            results.Select(result => result.Results[0].Outcome).Order().ToArray());

        await using var context = CreateContext();
        var logs = await context.CmsEventProcessingLogs.AsNoTracking()
            .Where(log => log.IdempotencyKey == $"external:{eventId}")
            .OrderByDescending(log => log.OwnsIdempotencyKey)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, logs.Length);
        Assert.Single(logs, log => log.OwnsIdempotencyKey);
        var owner = Assert.Single(logs, log => log.ReplayOfProcessingLogId is null);
        var replay = Assert.Single(logs, log => !log.OwnsIdempotencyKey);
        Assert.Equal(owner.ProcessingLogId, replay.ReplayOfProcessingLogId);
        Assert.Equal(1, await context.CmsEntityRevisions.CountAsync(
            revision => revision.EntityId == entityId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentCrossEntityEventIdRaceRollsBackLosingStateMutation()
    {
        await using var factory = CreateFactory();
        var firstEntityId = EventProcessingTestData.UniqueId("cross-entity-first");
        var secondEntityId = EventProcessingTestData.UniqueId("cross-entity-second");
        var eventId = EventProcessingTestData.UniqueId("shared-event");

        var results = await ProcessConcurrentlyAsync(
            factory.Services,
            EventProcessingTestData.Publish(firstEntityId, eventId: eventId),
            EventProcessingTestData.Publish(secondEntityId, eventId: eventId));

        Assert.Equal(
            AppliedAndConflict,
            results.Select(result => result.Results[0].Outcome).Order().ToArray());

        await using var context = CreateContext();
        var logs = await context.CmsEventProcessingLogs.AsNoTracking()
            .Where(log => log.IdempotencyKey == $"external:{eventId}")
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var entities = await context.CmsEntities.AsNoTracking()
            .Where(entity => entity.EntityId == firstEntityId || entity.EntityId == secondEntityId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var owner = Assert.Single(logs, log => log.OwnsIdempotencyKey);
        var replay = Assert.Single(logs, log => !log.OwnsIdempotencyKey);

        Assert.Equal(2, logs.Length);
        Assert.Equal(owner.ProcessingLogId, replay.ReplayOfProcessingLogId);
        Assert.Equal(ProcessingOutcome.Conflict.ToString(), replay.Outcome);
        Assert.Equal(owner.EntityId, Assert.Single(entities).EntityId);
    }

    [Fact]
    public async Task ConcurrentSameVersionDifferentPayloadAcceptsOneImmutablePayload()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("concurrent-payload");
        var firstEvent = EventProcessingTestData.Publish(
            entityId,
            eventId: EventProcessingTestData.UniqueId("event"),
            payload: "{\"winner\":1}");
        var secondEvent = EventProcessingTestData.Publish(
            entityId,
            eventId: EventProcessingTestData.UniqueId("event"),
            payload: "{\"winner\":2}");

        var results = await ProcessConcurrentlyAsync(factory.Services, firstEvent, secondEvent);

        Assert.Equal(
            AppliedAndConflict,
            results.Select(result => result.Results[0].Outcome).Order().ToArray());

        await using var context = CreateContext();
        var entity = await context.CmsEntities.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        var revision = await context.CmsEntityRevisions.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        Assert.Equal(entity.Payload, revision.FirstObservedPayload);
        Assert.True(entity.Payload is "{\"winner\":1}" or "{\"winner\":2}");
    }

    [Fact]
    public async Task ConcurrentHigherVersionsCannotRegressLatestVersionOrRowVersion()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("concurrent-version");
        await EventProcessingTestData.ProcessAsync(
            factory.Services,
            [EventProcessingTestData.Publish(entityId, eventId: EventProcessingTestData.UniqueId("event"))],
            cancellationToken: TestContext.Current.CancellationToken);
        var originalRowVersion = await ReadRowVersionAsync(entityId);

        var results = await ProcessConcurrentlyAsync(
            factory.Services,
            EventProcessingTestData.Publish(
                entityId,
                version: 2,
                eventId: EventProcessingTestData.UniqueId("event"),
                timestamp: "2026-08-02T11:00:00Z",
                payload: "{\"value\":2}"),
            EventProcessingTestData.Publish(
                entityId,
                version: 3,
                eventId: EventProcessingTestData.UniqueId("event"),
                timestamp: "2026-08-02T12:00:00Z",
                payload: "{\"value\":3}"));

        Assert.All(results, result => Assert.True(
            result.Results[0].Outcome is ProcessingOutcome.Applied or ProcessingOutcome.Stale));

        await using var context = CreateContext();
        var entity = await context.CmsEntities.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        Assert.Equal(3, entity.LatestVersion);
        Assert.NotEmpty(entity.RowVersion);
        Assert.NotEqual(originalRowVersion, entity.RowVersion);
    }

    [Fact]
    public async Task ConcurrentOlderOccurrenceHigherVersionsPreserveHighWatermark()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("concurrent-watermark");
        await EventProcessingTestData.ProcessAsync(
            factory.Services,
            [EventProcessingTestData.Publish(entityId, eventId: EventProcessingTestData.UniqueId("event"))],
            cancellationToken: TestContext.Current.CancellationToken);

        await ProcessConcurrentlyAsync(
            factory.Services,
            EventProcessingTestData.Publish(
                entityId,
                version: 2,
                eventId: EventProcessingTestData.UniqueId("event"),
                timestamp: "2026-08-02T09:00:00Z",
                payload: "{\"value\":2}"),
            EventProcessingTestData.Publish(
                entityId,
                version: 3,
                eventId: EventProcessingTestData.UniqueId("event"),
                timestamp: "2026-08-02T08:00:00Z",
                payload: "{\"value\":3}"));

        await using var context = CreateContext();
        var entity = await context.CmsEntities.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        Assert.Equal(3, entity.LatestVersion);
        Assert.Equal(new DateTime(2026, 8, 2, 8, 0, 0, DateTimeKind.Utc), entity.CurrentVersionOccurredAtUtc);
        Assert.Equal(new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc), entity.EntityEventHighWatermarkUtc);
    }

    [Fact]
    public async Task ConcurrentPublishAndBoundaryDeleteRespectHighWatermark()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("concurrent-delete-boundary");
        await EventProcessingTestData.ProcessAsync(
            factory.Services,
            [EventProcessingTestData.Publish(entityId, eventId: EventProcessingTestData.UniqueId("event"))],
            cancellationToken: TestContext.Current.CancellationToken);

        var results = await ProcessConcurrentlyAsync(
            factory.Services,
            EventProcessingTestData.Publish(
                entityId,
                version: 2,
                eventId: EventProcessingTestData.UniqueId("event"),
                timestamp: "2026-08-02T09:00:00Z",
                payload: "{\"value\":2}"),
            EventProcessingTestData.Delete(
                entityId,
                EventProcessingTestData.UniqueId("event"),
                "2026-08-02T10:00:00Z"));

        Assert.Contains(results, result => result.Results[0].Outcome == ProcessingOutcome.Applied);
        Assert.Contains(results, result => result.Results[0].Outcome == ProcessingOutcome.Conflict);

        await using var context = CreateContext();
        var entity = await context.CmsEntities.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        Assert.Equal(2, entity.LatestVersion);
        Assert.Equal(new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc), entity.EntityEventHighWatermarkUtc);
        Assert.False(await context.CmsDeletionTombstones.AnyAsync(
            tombstone => tombstone.EntityId == entityId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentDeleteAndRecreationProduceAValidSerialGeneration()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("concurrent-recreate");
        await EventProcessingTestData.ProcessAsync(
            factory.Services,
            [EventProcessingTestData.Publish(entityId, eventId: EventProcessingTestData.UniqueId("event"))],
            cancellationToken: TestContext.Current.CancellationToken);

        var results = await ProcessConcurrentlyAsync(
            factory.Services,
            EventProcessingTestData.Delete(
                entityId,
                EventProcessingTestData.UniqueId("event"),
                "2026-08-02T11:00:00Z"),
            EventProcessingTestData.Publish(
                entityId,
                version: 7,
                eventId: EventProcessingTestData.UniqueId("event"),
                timestamp: "2026-08-02T12:00:00Z",
                payload: "{\"value\":7}"));

        Assert.Contains(results, result => result.Results[0].Outcome == ProcessingOutcome.Applied);

        await using var context = CreateContext();
        var entity = await context.CmsEntities.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        var tombstone = await context.CmsDeletionTombstones.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        Assert.Equal(7, entity.LatestVersion);

        if (tombstone is null)
        {
            Assert.Equal(1, entity.Generation);
            Assert.Contains(results, result => result.Results[0].Outcome == ProcessingOutcome.Stale);
        }
        else
        {
            Assert.Equal(1, tombstone.LastDeletedGeneration);
            Assert.Equal(2, entity.Generation);
            Assert.All(results, result => Assert.Equal(ProcessingOutcome.Applied, result.Results[0].Outcome));
        }
    }

    private static async Task<CmsEventBatchResult[]> ProcessConcurrentlyAsync(
        IServiceProvider services,
        string firstEvent,
        string secondEvent)
    {
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = ProcessAfterGateAsync(services, firstEvent, startGate.Task);
        var secondTask = ProcessAfterGateAsync(services, secondEvent, startGate.Task);

        startGate.SetResult();

        return await Task.WhenAll(firstTask, secondTask);
    }

    private static async Task<CmsEventBatchResult> ProcessAfterGateAsync(
        IServiceProvider services,
        string eventJson,
        Task startGate)
    {
        await startGate;
        return await EventProcessingTestData.ProcessAsync(
            services,
            [eventJson],
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private async Task<byte[]> ReadRowVersionAsync(string entityId)
    {
        await using var context = CreateContext();
        return await context.CmsEntities.AsNoTracking()
            .Where(entity => entity.EntityId == entityId)
            .Select(entity => entity.RowVersion)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private CmsWriteDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CmsWriteDbContext>()
            .UseSqlServer(_fixture.WriteConnectionString)
            .Options;
        return new CmsWriteDbContext(options);
    }

    private CmsSyncWebApplicationFactory CreateFactory()
    {
        return new CmsSyncWebApplicationFactory(
            _fixture.WriteConnectionString,
            _fixture.ReadConnectionString);
    }
}
