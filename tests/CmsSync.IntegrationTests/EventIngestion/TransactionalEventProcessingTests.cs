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
public sealed class TransactionalEventProcessingTests
{
    private static readonly int[] ThreeSequences = [0, 1, 2];

    private readonly SqlServerFixture _fixture;

    public TransactionalEventProcessingTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ValidItemCreatesStateRevisionAndIdentityOwnerLogAtomically()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("transactional-create");
        var eventId = EventProcessingTestData.UniqueId("event");
        var batchId = Guid.NewGuid();

        var result = await EventProcessingTestData.ProcessAsync(
            factory.Services,
            [EventProcessingTestData.Publish(entityId, version: 7, eventId: eventId)],
            batchId,
            cancellationToken: TestContext.Current.CancellationToken);

        var item = Assert.Single(result.Results);
        Assert.Equal(ProcessingOutcome.Applied, item.Outcome);
        Assert.Equal("ENTITY_CREATED", item.Code);
        Assert.Equal(1, item.Generation);
        Assert.Equal(7, item.ResultingVersion);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var entity = await context.CmsEntities.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        var revision = await context.CmsEntityRevisions.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        var log = await context.CmsEventProcessingLogs.AsNoTracking().SingleAsync(
            candidate => candidate.BatchId == batchId && candidate.Sequence == 0,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, entity.Generation);
        Assert.Equal(7, entity.LatestVersion);
        Assert.Equal(7, revision.Version);
        Assert.True(log.OwnsIdempotencyKey);
        Assert.Equal($"external:{eventId}", log.IdempotencyKey);
        Assert.Null(log.ReplayOfProcessingLogId);
        Assert.NotNull(log.EventContentHash);
        Assert.DoesNotContain("payload", log.Code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidItemWithoutIdentityCreatesOnlyOnePayloadFreeAttempt()
    {
        await using var factory = CreateFactory();
        var batchId = Guid.NewGuid();

        var result = await EventProcessingTestData.ProcessAsync(
            factory.Services,
            ["{\"type\":\"publish\"}"],
            batchId,
            cancellationToken: TestContext.Current.CancellationToken);

        var item = Assert.Single(result.Results);
        Assert.Equal(ProcessingOutcome.Invalid, item.Outcome);
        Assert.Equal("ENTITY_ID_REQUIRED", item.Code);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var log = await context.CmsEventProcessingLogs.AsNoTracking().SingleAsync(
            candidate => candidate.BatchId == batchId,
            TestContext.Current.CancellationToken);

        Assert.False(log.OwnsIdempotencyKey);
        Assert.Null(log.IdempotencyKey);
        Assert.Null(log.ReplayOfProcessingLogId);
        Assert.Null(log.EventContentHash);
        Assert.Null(log.PayloadHash);
        Assert.Null(log.EntityId);
        Assert.Empty(await context.CmsEntities.Where(entity => entity.EntityId == log.EntityId).ToArrayAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MixedBatchCommitsEveryCompletedPositionInSequence()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("mixed-batch");
        var batchId = Guid.NewGuid();
        var events = new[]
        {
            EventProcessingTestData.Publish(
                entityId,
                version: 1,
                eventId: EventProcessingTestData.UniqueId("event")),
            "{\"type\":\"unsupported\"}",
            EventProcessingTestData.Publish(
                entityId,
                version: 3,
                eventId: EventProcessingTestData.UniqueId("event"),
                timestamp: "2026-08-02T11:00:00Z",
                payload: "{\"value\":3}"),
        };

        var result = await EventProcessingTestData.ProcessAsync(
            factory.Services,
            events,
            batchId,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { ProcessingOutcome.Applied, ProcessingOutcome.Invalid, ProcessingOutcome.Applied },
            result.Results.Select(item => item.Outcome));

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var logs = await context.CmsEventProcessingLogs.AsNoTracking()
            .Where(log => log.BatchId == batchId)
            .OrderBy(log => log.Sequence)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var entity = await context.CmsEntities.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);

        Assert.Equal(ThreeSequences, logs.Select(log => log.Sequence));
        Assert.Equal(3, entity.LatestVersion);
        Assert.Equal(2, await context.CmsEntityRevisions.CountAsync(
            revision => revision.EntityId == entityId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ProductionDependencyInjectionResolvesIndependentScopedServicesAndExecutor()
    {
        using var factory = CreateFactory();
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var firstService = firstScope.ServiceProvider.GetRequiredService<CmsEventBatchService>();
        var sameService = firstScope.ServiceProvider.GetRequiredService<CmsEventBatchService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<CmsEventBatchService>();
        var firstExecutor = firstScope.ServiceProvider.GetRequiredService<IEventTransactionExecutor>();
        var secondExecutor = secondScope.ServiceProvider.GetRequiredService<IEventTransactionExecutor>();

        Assert.Same(firstService, sameService);
        Assert.NotSame(firstService, secondService);
        Assert.IsType<SqlServerEventTransactionExecutor>(firstExecutor);
        Assert.IsType<SqlServerEventTransactionExecutor>(secondExecutor);
        Assert.NotSame(firstExecutor, secondExecutor);
        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<TimeProvider>(),
            secondScope.ServiceProvider.GetRequiredService<TimeProvider>());
    }

    private CmsSyncWebApplicationFactory CreateFactory()
    {
        return new CmsSyncWebApplicationFactory(
            _fixture.WriteConnectionString,
            _fixture.ReadConnectionString);
    }
}
