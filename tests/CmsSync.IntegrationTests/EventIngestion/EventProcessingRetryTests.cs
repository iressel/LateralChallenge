using System.Data;
using CmsSync.Application.EventIngestion;
using CmsSync.Domain.Processing;
using CmsSync.Infrastructure.Persistence;
using CmsSync.Infrastructure.Persistence.EventProcessing;
using CmsSync.IntegrationTests.Infrastructure;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.EventIngestion;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "EventProcessing")]
public sealed class EventProcessingRetryTests
{
    private static readonly int[] FirstTwoSequences = [0, 1];
    private static readonly int[] FirstSequence = [0];

    private readonly SqlServerFixture _fixture;

    public EventProcessingRetryTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TransientFailureBeforeCommitRetriesFreshTransactionAndMutatesOnce()
    {
        var interceptor = new TransientBeforeCommitInterceptor();
        var executor = EventProcessingExecutorFactory.Create(
            _fixture.WriteConnectionString,
            [interceptor],
            useTestExecutionStrategy: true);
        var service = new CmsEventBatchService(executor);
        var entityId = EventProcessingTestData.UniqueId("retry-once");
        var request = EventProcessingTestData.CreateRequest(
            [EventProcessingTestData.Publish(entityId, eventId: EventProcessingTestData.UniqueId("event"))]);

        var result = await service.ProcessAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(ProcessingOutcome.Applied, Assert.Single(result.Results).Outcome);
        Assert.Equal(2, interceptor.StartedTransactions);
        Assert.Equal(1, interceptor.CommittedTransactions);

        await using var context = CreateContext();
        Assert.Equal(1, await context.CmsEntities.CountAsync(
            entity => entity.EntityId == entityId,
            TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.CmsEntityRevisions.CountAsync(
            revision => revision.EntityId == entityId,
            TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.CmsEventProcessingLogs.CountAsync(
            log => log.BatchId == request.BatchId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AmbiguousCommitRecoversBatchPositionWithoutDuplicateMutationOrLog()
    {
        var interceptor = new AmbiguousCommitInterceptor();
        var executor = EventProcessingExecutorFactory.Create(
            _fixture.WriteConnectionString,
            [interceptor],
            useTestExecutionStrategy: true);
        var service = new CmsEventBatchService(executor);
        var entityId = EventProcessingTestData.UniqueId("ambiguous-commit");
        var eventId = EventProcessingTestData.UniqueId("event");
        var eventJson = EventProcessingTestData.Publish(entityId, eventId: eventId);
        var request = EventProcessingTestData.CreateRequest([eventJson]);

        var recoveredResult = await service.ProcessAsync(
            request,
            TestContext.Current.CancellationToken);

        var recoveredItem = Assert.Single(recoveredResult.Results);
        Assert.Equal(ProcessingOutcome.Applied, recoveredItem.Outcome);
        Assert.Equal(2, interceptor.CommittedTransactions);

        await using (var context = CreateContext())
        {
            Assert.Equal(1, await context.CmsEntities.CountAsync(
                entity => entity.EntityId == entityId,
                TestContext.Current.CancellationToken));
            Assert.Equal(1, await context.CmsEntityRevisions.CountAsync(
                revision => revision.EntityId == entityId,
                TestContext.Current.CancellationToken));
            Assert.Equal(1, await context.CmsEventProcessingLogs.CountAsync(
                log => log.BatchId == request.BatchId,
                TestContext.Current.CancellationToken));
        }

        var replayResult = await new CmsEventBatchService(
            EventProcessingExecutorFactory.Create(_fixture.WriteConnectionString))
            .ProcessAsync(
                EventProcessingTestData.CreateRequest([eventJson]),
                TestContext.Current.CancellationToken);

        Assert.Equal(ProcessingOutcome.Duplicate, Assert.Single(replayResult.Results).Outcome);

        await using var replayContext = CreateContext();
        Assert.Equal(2, await replayContext.CmsEventProcessingLogs.CountAsync(
            log => log.EntityId == entityId,
            TestContext.Current.CancellationToken));
        Assert.Equal(1, await replayContext.CmsEventProcessingLogs.CountAsync(
            log => log.EntityId == entityId && log.OwnsIdempotencyKey,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailureAfterStateStagingBeforeCommitLeavesNoStateOrLog()
    {
        var executor = EventProcessingExecutorFactory.Create(
            _fixture.WriteConnectionString,
            [new TerminalBeforeCommitInterceptor()]);
        var service = new CmsEventBatchService(executor);
        var entityId = EventProcessingTestData.UniqueId("atomic-rollback");
        var request = EventProcessingTestData.CreateRequest(
            [EventProcessingTestData.Publish(entityId, eventId: EventProcessingTestData.UniqueId("event"))]);

        await Assert.ThrowsAsync<InjectedTerminalEventProcessingException>(() =>
            service.ProcessAsync(request, TestContext.Current.CancellationToken));

        await using var context = CreateContext();
        Assert.False(await context.CmsEntities.AnyAsync(
            entity => entity.EntityId == entityId,
            TestContext.Current.CancellationToken));
        Assert.False(await context.CmsEntityRevisions.AnyAsync(
            revision => revision.EntityId == entityId,
            TestContext.Current.CancellationToken));
        Assert.False(await context.CmsEventProcessingLogs.AnyAsync(
            log => log.BatchId == request.BatchId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExhaustedApplicationLockFailureIsSafeAndStopsLaterPositions()
    {
        var lockedEntityId = EventProcessingTestData.UniqueId("lock-exhausted");
        var laterEntityId = EventProcessingTestData.UniqueId("lock-later");
        await using var holderConnection = new SqlConnection(_fixture.WriteConnectionString);
        await holderConnection.OpenAsync(TestContext.Current.CancellationToken);
        await using var holderTransaction = await holderConnection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            TestContext.Current.CancellationToken);
        await new SqlServerEntityApplicationLock().AcquireAsync(
            holderConnection,
            holderTransaction,
            lockedEntityId,
            TestContext.Current.CancellationToken);

        var executor = EventProcessingExecutorFactory.Create(
            _fixture.WriteConnectionString,
            applicationLock: new SqlServerEntityApplicationLock(TimeSpan.FromMilliseconds(50)));
        var service = new CmsEventBatchService(executor);
        var request = EventProcessingTestData.CreateRequest(
            [
                EventProcessingTestData.Publish(
                    lockedEntityId,
                    eventId: EventProcessingTestData.UniqueId("event")),
                EventProcessingTestData.Publish(
                    laterEntityId,
                    eventId: EventProcessingTestData.UniqueId("event")),
            ]);

        await Assert.ThrowsAsync<EventProcessingDependencyUnavailableException>(() =>
            service.ProcessAsync(request, TestContext.Current.CancellationToken));
        await holderTransaction.RollbackAsync(TestContext.Current.CancellationToken);

        await using var context = CreateContext();
        Assert.False(await context.CmsEntities.AnyAsync(
            entity => entity.EntityId == lockedEntityId || entity.EntityId == laterEntityId,
            TestContext.Current.CancellationToken));
        Assert.False(await context.CmsEventProcessingLogs.AnyAsync(
            log => log.BatchId == request.BatchId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LaterTerminalFailurePreservesPriorCommitAndWholeRequestRetryRecoversIt()
    {
        await using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var productionExecutor = scope.ServiceProvider.GetRequiredService<IEventTransactionExecutor>();
        var failingExecutor = new FailOnSequenceEventTransactionExecutor(productionExecutor, failingSequence: 1);
        var failingService = new CmsEventBatchService(failingExecutor);
        var entityIds = new[]
        {
            EventProcessingTestData.UniqueId("failure-first"),
            EventProcessingTestData.UniqueId("failure-second"),
            EventProcessingTestData.UniqueId("failure-third"),
        };
        var events = entityIds
            .Select(entityId => EventProcessingTestData.Publish(
                entityId,
                eventId: EventProcessingTestData.UniqueId("event")))
            .ToArray();
        var request = EventProcessingTestData.CreateRequest(events);

        await Assert.ThrowsAsync<InjectedTerminalEventProcessingException>(() =>
            failingService.ProcessAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(FirstTwoSequences, failingExecutor.InvokedSequences);

        await using (var failedContext = CreateContext())
        {
            Assert.True(await failedContext.CmsEntities.AnyAsync(
                entity => entity.EntityId == entityIds[0],
                TestContext.Current.CancellationToken));
            Assert.False(await failedContext.CmsEntities.AnyAsync(
                entity => entity.EntityId == entityIds[1] || entity.EntityId == entityIds[2],
                TestContext.Current.CancellationToken));
            Assert.Equal(1, await failedContext.CmsEventProcessingLogs.CountAsync(
                log => log.BatchId == request.BatchId,
                TestContext.Current.CancellationToken));
        }

        var recovered = await new CmsEventBatchService(productionExecutor).ProcessAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(3, recovered.Results.Count);
        Assert.All(recovered.Results, item => Assert.Equal(ProcessingOutcome.Applied, item.Outcome));

        await using var recoveredContext = CreateContext();
        Assert.Equal(3, await recoveredContext.CmsEventProcessingLogs.CountAsync(
            log => log.BatchId == request.BatchId,
            TestContext.Current.CancellationToken));
        Assert.Equal(3, await recoveredContext.CmsEntities.CountAsync(
            entity => entityIds.Contains(entity.EntityId),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancellationAfterFirstCommitPreservesItAndDoesNotStartLaterPositions()
    {
        await using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var productionExecutor = scope.ServiceProvider.GetRequiredService<IEventTransactionExecutor>();
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var cancelingExecutor = new CancelAfterSequenceEventTransactionExecutor(
            productionExecutor,
            cancellationSource,
            cancellationSequence: 0);
        var service = new CmsEventBatchService(cancelingExecutor);
        var firstEntityId = EventProcessingTestData.UniqueId("cancel-first");
        var secondEntityId = EventProcessingTestData.UniqueId("cancel-second");
        var request = EventProcessingTestData.CreateRequest(
            [
                EventProcessingTestData.Publish(
                    firstEntityId,
                    eventId: EventProcessingTestData.UniqueId("event")),
                EventProcessingTestData.Publish(
                    secondEntityId,
                    eventId: EventProcessingTestData.UniqueId("event")),
            ]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ProcessAsync(request, cancellationSource.Token));

        Assert.Equal(FirstSequence, cancelingExecutor.InvokedSequences);

        await using var context = CreateContext();
        Assert.True(await context.CmsEntities.AnyAsync(
            entity => entity.EntityId == firstEntityId,
            TestContext.Current.CancellationToken));
        Assert.False(await context.CmsEntities.AnyAsync(
            entity => entity.EntityId == secondEntityId,
            TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.CmsEventProcessingLogs.CountAsync(
            log => log.BatchId == request.BatchId,
            TestContext.Current.CancellationToken));
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
