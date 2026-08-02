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
public sealed class IdempotencyReplayTests
{
    private readonly SqlServerFixture _fixture;

    public IdempotencyReplayTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ExactExternalEventIdReplayCreatesOneOwnerAndOneReferencedNonOwner()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("external-replay");
        var eventId = EventProcessingTestData.UniqueId("event");
        var eventJson = EventProcessingTestData.Publish(entityId, eventId: eventId);

        var original = await ProcessSingleAsync(factory, eventJson);
        var replay = await ProcessSingleAsync(factory, eventJson);

        Assert.Equal(ProcessingOutcome.Applied, original.Outcome);
        Assert.Equal(ProcessingOutcome.Duplicate, replay.Outcome);
        Assert.Equal(EventProcessingCodes.ExactDuplicate, replay.Code);
        var logs = await ReadIdentityLogsAsync(factory, $"external:{eventId}");
        AssertOwnerAndReplay(logs);
        Assert.Equal(1, await CountRevisionsAsync(factory, entityId));
    }

    [Fact]
    public async Task ExactDerivedIdentityReplayHasTheSameOwnerAndReplayBehavior()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("derived-replay");
        var eventJson = EventProcessingTestData.Publish(entityId);

        var original = await ProcessSingleAsync(factory, eventJson);
        var replay = await ProcessSingleAsync(factory, eventJson);

        Assert.Equal(ProcessingOutcome.Applied, original.Outcome);
        Assert.Equal(ProcessingOutcome.Duplicate, replay.Outcome);
        var logs = await ReadEntityLogsAsync(factory, entityId);
        Assert.StartsWith("sha256:", logs[0].IdempotencyKey, StringComparison.Ordinal);
        AssertOwnerAndReplay(logs);
        Assert.Equal(1, await CountRevisionsAsync(factory, entityId));
    }

    [Fact]
    public async Task ExternalEventIdReuseWithDifferentContentConflictsBeforeStateMutation()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("event-id-conflict");
        var eventId = EventProcessingTestData.UniqueId("event");
        var ownerEvent = EventProcessingTestData.Publish(entityId, eventId: eventId);
        var conflictingEvent = EventProcessingTestData.Publish(
            entityId,
            version: 2,
            eventId: eventId,
            timestamp: "2026-08-02T11:00:00Z",
            payload: "{\"value\":2}");

        _ = await ProcessSingleAsync(factory, ownerEvent);
        var conflict = await ProcessSingleAsync(factory, conflictingEvent);

        Assert.Equal(ProcessingOutcome.Conflict, conflict.Outcome);
        Assert.Equal(EventProcessingCodes.EventIdContentConflict, conflict.Code);
        var logs = await ReadIdentityLogsAsync(factory, $"external:{eventId}");
        AssertOwnerAndReplay(logs);
        Assert.Equal("EVENT_ID_CONTENT_CONFLICT", logs[1].Code);
        Assert.Equal(1, await ReadLatestVersionAsync(factory, entityId));
        Assert.Equal(1, await CountRevisionsAsync(factory, entityId));
    }

    [Fact]
    public async Task ExactReplayOfOriginalDomainConflictPreservesItsOutcomeAndCode()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("conflict-replay");
        var initial = EventProcessingTestData.Publish(
            entityId,
            eventId: EventProcessingTestData.UniqueId("initial"));
        var conflictingEventId = EventProcessingTestData.UniqueId("conflict");
        var conflictJson = EventProcessingTestData.Publish(
            entityId,
            eventId: conflictingEventId,
            payload: "{\"value\":99}");

        _ = await ProcessSingleAsync(factory, initial);
        var originalConflict = await ProcessSingleAsync(factory, conflictJson);
        var replayConflict = await ProcessSingleAsync(factory, conflictJson);

        Assert.Equal(ProcessingOutcome.Conflict, originalConflict.Outcome);
        Assert.Equal("PAYLOAD_CONFLICT", originalConflict.Code);
        Assert.Equal(originalConflict.Outcome, replayConflict.Outcome);
        Assert.Equal(originalConflict.Code, replayConflict.Code);
        AssertOwnerAndReplay(await ReadIdentityLogsAsync(
            factory,
            $"external:{conflictingEventId}"));
        Assert.Equal(1, await CountRevisionsAsync(factory, entityId));
    }

    [Fact]
    public async Task ExactReplayOfOversizedPayloadPreservesOriginalInvalidOutcomeAndCode()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("invalid-replay");
        var eventId = EventProcessingTestData.UniqueId("invalid-event");
        var oversizedPayload =
            "{\"data\":\"" +
            new string('a', CmsEventIngestionLimits.AbsoluteMaximumPayloadSizeBytes) +
            "\"}";
        var invalidJson = EventProcessingTestData.Publish(
            entityId,
            eventId: eventId,
            payload: oversizedPayload);

        var originalInvalid = await ProcessSingleAsync(factory, invalidJson);
        var replayInvalid = await ProcessSingleAsync(factory, invalidJson);

        Assert.Equal(ProcessingOutcome.Invalid, originalInvalid.Outcome);
        Assert.Equal(EventValidationCodes.PayloadTooLarge, originalInvalid.Code);
        Assert.Equal(originalInvalid.Outcome, replayInvalid.Outcome);
        Assert.Equal(originalInvalid.Code, replayInvalid.Code);
        var logs = await ReadIdentityLogsAsync(factory, $"external:{eventId}");
        AssertOwnerAndReplay(logs);
        Assert.All(logs, log => Assert.NotNull(log.EventContentHash));
        Assert.All(logs, log => Assert.NotNull(log.PayloadHash));
        Assert.Equal(0, await CountRevisionsAsync(factory, entityId));
    }

    [Fact]
    public async Task InvalidEventsWithoutIdentityProduceIndependentNonOwnerAttempts()
    {
        await using var factory = CreateFactory();
        var firstBatch = Guid.NewGuid();
        var secondBatch = Guid.NewGuid();
        const string invalidJson = "{\"type\":\"publish\"}";

        _ = await EventProcessingTestData.ProcessAsync(
            factory.Services,
            [invalidJson],
            firstBatch,
            cancellationToken: TestContext.Current.CancellationToken);
        _ = await EventProcessingTestData.ProcessAsync(
            factory.Services,
            [invalidJson],
            secondBatch,
            cancellationToken: TestContext.Current.CancellationToken);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var logs = await context.CmsEventProcessingLogs.AsNoTracking()
            .Where(log => log.BatchId == firstBatch || log.BatchId == secondBatch)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, logs.Length);
        Assert.All(logs, log => Assert.False(log.OwnsIdempotencyKey));
        Assert.All(logs, log => Assert.Null(log.IdempotencyKey));
        Assert.All(logs, log => Assert.Null(log.ReplayOfProcessingLogId));
    }

    [Fact]
    public async Task DifferentIdentityForRepresentedStateIsEquivalentAndOwnsItsIdentity()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("equivalent-state");
        var firstEventId = EventProcessingTestData.UniqueId("first");
        var secondEventId = EventProcessingTestData.UniqueId("second");
        var firstJson = EventProcessingTestData.Publish(entityId, eventId: firstEventId);
        var secondJson = EventProcessingTestData.Publish(entityId, eventId: secondEventId);

        _ = await ProcessSingleAsync(factory, firstJson);
        var equivalent = await ProcessSingleAsync(factory, secondJson);

        Assert.Equal(ProcessingOutcome.Equivalent, equivalent.Outcome);
        Assert.Equal("STATE_EQUIVALENT", equivalent.Code);
        var secondLogs = await ReadIdentityLogsAsync(factory, $"external:{secondEventId}");
        var log = Assert.Single(secondLogs);
        Assert.True(log.OwnsIdempotencyKey);
        Assert.Null(log.ReplayOfProcessingLogId);
        Assert.Equal(1, await CountRevisionsAsync(factory, entityId));
    }

    [Fact]
    public async Task SameBatchPositionRecoveryReturnsOriginalResultWithoutAnotherLogOrMutation()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("batch-recovery");
        var eventId = EventProcessingTestData.UniqueId("event");
        var eventJson = EventProcessingTestData.Publish(entityId, eventId: eventId);
        var batchId = Guid.NewGuid();
        var correlationId = EventProcessingTestData.UniqueId("correlation");
        var subject = EventProcessingTestData.UniqueId("subject");

        var original = await EventProcessingTestData.ProcessAsync(
            factory.Services,
            [eventJson],
            batchId,
            correlationId,
            subject,
            TestContext.Current.CancellationToken);
        var recovered = await EventProcessingTestData.ProcessAsync(
            factory.Services,
            [eventJson],
            batchId,
            correlationId,
            subject,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessingOutcome.Applied, Assert.Single(original.Results).Outcome);
        Assert.Equal(ProcessingOutcome.Applied, Assert.Single(recovered.Results).Outcome);
        Assert.Equal("ENTITY_CREATED", Assert.Single(recovered.Results).Code);
        Assert.Equal(1, await CountBatchLogsAsync(factory, batchId));
        Assert.Equal(1, await CountRevisionsAsync(factory, entityId));
    }

    [Fact]
    public async Task SameBatchPositionWithDifferentEventFailsSafelyWithoutSecondMutation()
    {
        await using var factory = CreateFactory();
        var firstEntity = EventProcessingTestData.UniqueId("batch-first");
        var secondEntity = EventProcessingTestData.UniqueId("batch-second");
        var batchId = Guid.NewGuid();
        var correlationId = EventProcessingTestData.UniqueId("correlation");
        var subject = EventProcessingTestData.UniqueId("subject");

        _ = await EventProcessingTestData.ProcessAsync(
            factory.Services,
            [EventProcessingTestData.Publish(firstEntity)],
            batchId,
            correlationId,
            subject,
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EventProcessingTestData.ProcessAsync(
                factory.Services,
                [EventProcessingTestData.Publish(secondEntity)],
                batchId,
                correlationId,
                subject,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            "The completed batch position is inconsistent with the current event request.",
            exception.Message);
        Assert.Equal(1, await CountBatchLogsAsync(factory, batchId));
        Assert.Equal(0, await CountRevisionsAsync(factory, secondEntity));
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

    private static async Task<CmsEventProcessingLog[]> ReadIdentityLogsAsync(
        CmsSyncWebApplicationFactory factory,
        string idempotencyKey)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEventProcessingLogs.AsNoTracking()
            .Where(log => log.IdempotencyKey == idempotencyKey)
            .OrderBy(log => log.ProcessingLogId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<CmsEventProcessingLog[]> ReadEntityLogsAsync(
        CmsSyncWebApplicationFactory factory,
        string entityId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEventProcessingLogs.AsNoTracking()
            .Where(log => log.EntityId == entityId)
            .OrderBy(log => log.ProcessingLogId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
    }

    private static void AssertOwnerAndReplay(CmsEventProcessingLog[] logs)
    {
        Assert.Equal(2, logs.Length);
        var owner = Assert.Single(logs, log => log.OwnsIdempotencyKey);
        var replay = Assert.Single(logs, log => !log.OwnsIdempotencyKey);
        Assert.Null(owner.ReplayOfProcessingLogId);
        Assert.Equal(owner.ProcessingLogId, replay.ReplayOfProcessingLogId);
        Assert.Equal(owner.IdempotencyKey, replay.IdempotencyKey);
    }

    private static async Task<int> CountRevisionsAsync(
        CmsSyncWebApplicationFactory factory,
        string entityId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEntityRevisions.CountAsync(
            revision => revision.EntityId == entityId,
            TestContext.Current.CancellationToken);
    }

    private static async Task<long> ReadLatestVersionAsync(
        CmsSyncWebApplicationFactory factory,
        string entityId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEntities
            .Where(entity => entity.EntityId == entityId)
            .Select(entity => entity.LatestVersion)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<int> CountBatchLogsAsync(
        CmsSyncWebApplicationFactory factory,
        Guid batchId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEventProcessingLogs.CountAsync(
            log => log.BatchId == batchId,
            TestContext.Current.CancellationToken);
    }

    private CmsSyncWebApplicationFactory CreateFactory()
    {
        return new CmsSyncWebApplicationFactory(
            _fixture.WriteConnectionString,
            _fixture.ReadConnectionString);
    }
}
