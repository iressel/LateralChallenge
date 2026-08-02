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
public sealed class StateRevisionProcessingTests
{
    private readonly SqlServerFixture _fixture;

    public StateRevisionProcessingTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FirstUnpublishCreatesGenerationOneAndRemainsActiveButUnpublished()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("first-unpublish");

        var item = await ProcessSingleAsync(
            factory,
            EventProcessingTestData.Publish(
                entityId,
                version: 7,
                eventId: EventProcessingTestData.UniqueId("event"),
                type: "unpublish"));

        Assert.Equal(ProcessingOutcome.Applied, item.Outcome);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var entity = await context.CmsEntities.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        Assert.Equal(1, entity.Generation);
        Assert.Equal(7, entity.LatestVersion);
        Assert.Equal("Unpublished", entity.CmsPublicationStatus);
        Assert.Equal(1, await context.CmsEntityRevisions.CountAsync(
            revision => revision.EntityId == entityId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HigherSkippedVersionAddsImmutableRevisionAndPreservesAdministrativeDisable()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("version-advance");
        var firstPayload = "{\"value\":1}";
        var laterPayload = "{\"value\":10}";

        _ = await ProcessSingleAsync(
            factory,
            EventProcessingTestData.Publish(
                entityId,
                version: 7,
                eventId: EventProcessingTestData.UniqueId("event"),
                payload: firstPayload));
        await SetAdministrativeDisabledAsync(factory, entityId);
        var advanced = await ProcessSingleAsync(
            factory,
            EventProcessingTestData.Publish(
                entityId,
                version: 10,
                eventId: EventProcessingTestData.UniqueId("event"),
                timestamp: "2026-08-02T11:00:00Z",
                payload: laterPayload,
                type: "unpublish"));

        Assert.Equal(ProcessingOutcome.Applied, advanced.Outcome);
        Assert.Equal("VERSION_ADVANCED", advanced.Code);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var entity = await context.CmsEntities.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        var revisions = await context.CmsEntityRevisions.AsNoTracking()
            .Where(revision => revision.EntityId == entityId)
            .OrderBy(revision => revision.Version)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(10, entity.LatestVersion);
        Assert.Equal("Unpublished", entity.CmsPublicationStatus);
        Assert.True(entity.AdministrativeDisabled);
        Assert.NotNull(entity.AdministrativeStateChangedAtUtc);
        Assert.Equal("test-administrator", entity.AdministrativeStateChangedBy);
        Assert.Equal(new long[] { 7, 10 }, revisions.Select(revision => revision.Version));
        Assert.Equal(firstPayload, revisions[0].FirstObservedPayload);
        Assert.Equal(laterPayload, revisions[1].FirstObservedPayload);
    }

    [Fact]
    public async Task LowerVersionIsStaleAndDoesNotChangeStateOrRevisionHistory()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("lower-version");

        _ = await ProcessSingleAsync(
            factory,
            EventProcessingTestData.Publish(
                entityId,
                version: 5,
                eventId: EventProcessingTestData.UniqueId("event")));
        var stale = await ProcessSingleAsync(
            factory,
            EventProcessingTestData.Publish(
                entityId,
                version: 4,
                eventId: EventProcessingTestData.UniqueId("event"),
                timestamp: "2026-08-02T12:00:00Z",
                payload: "{\"value\":4}"));

        Assert.Equal(ProcessingOutcome.Stale, stale.Outcome);
        Assert.Equal("VERSION_STALE", stale.Code);
        Assert.Equal(5, await ReadLatestVersionAsync(factory, entityId));
        Assert.Equal(1, await CountRevisionsAsync(factory, entityId));
    }

    [Fact]
    public async Task SameVersionLaterTimestampChangesStatusWithoutUpdatingImmutableRevision()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("same-version-later");
        const string firstPayload = "{ \"value\" : 1 }";
        const string equivalentPayload = "{\"value\":1}";

        _ = await ProcessSingleAsync(
            factory,
            EventProcessingTestData.Publish(
                entityId,
                eventId: EventProcessingTestData.UniqueId("event"),
                payload: firstPayload));
        var changed = await ProcessSingleAsync(
            factory,
            EventProcessingTestData.Publish(
                entityId,
                eventId: EventProcessingTestData.UniqueId("event"),
                timestamp: "2026-08-02T11:00:00Z",
                payload: equivalentPayload,
                type: "unpublish"));

        Assert.Equal(ProcessingOutcome.Applied, changed.Outcome);
        Assert.Equal("SAME_VERSION_APPLIED", changed.Code);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var entity = await context.CmsEntities.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        var revision = await context.CmsEntityRevisions.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);

        Assert.Equal("Unpublished", entity.CmsPublicationStatus);
        Assert.Equal(new DateTime(2026, 8, 2, 11, 0, 0, DateTimeKind.Utc), entity.CurrentVersionOccurredAtUtc);
        Assert.Equal(firstPayload, entity.Payload);
        Assert.Equal(firstPayload, revision.FirstObservedPayload);
        Assert.Equal(1, await CountRevisionsAsync(factory, entityId));
    }

    [Fact]
    public async Task HigherVersionWithOlderTimestampMovesCurrentTimestampButNotHighWatermark()
    {
        await using var factory = CreateFactory();
        var entityId = EventProcessingTestData.UniqueId("older-timestamp");

        _ = await ProcessSingleAsync(
            factory,
            EventProcessingTestData.Publish(
                entityId,
                version: 5,
                eventId: EventProcessingTestData.UniqueId("event"),
                timestamp: "2026-08-02T10:00:00Z",
                payload: "{\"version\":5}"));
        var advanced = await ProcessSingleAsync(
            factory,
            EventProcessingTestData.Publish(
                entityId,
                version: 6,
                eventId: EventProcessingTestData.UniqueId("event"),
                timestamp: "2026-08-02T09:00:00Z",
                payload: "{\"version\":6}"));

        Assert.Equal(ProcessingOutcome.Applied, advanced.Outcome);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var entity = await context.CmsEntities.AsNoTracking().SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);

        Assert.Equal(6, entity.LatestVersion);
        Assert.Equal(new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc), entity.CurrentVersionOccurredAtUtc);
        Assert.Equal(new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc), entity.EntityEventHighWatermarkUtc);
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

    private static async Task SetAdministrativeDisabledAsync(
        CmsSyncWebApplicationFactory factory,
        string entityId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var entity = await context.CmsEntities.SingleAsync(
            candidate => candidate.EntityId == entityId,
            TestContext.Current.CancellationToken);
        entity.AdministrativeDisabled = true;
        entity.AdministrativeStateChangedAtUtc = new DateTime(2026, 8, 2, 10, 30, 0, DateTimeKind.Utc);
        entity.AdministrativeStateChangedBy = "test-administrator";
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
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

    private CmsSyncWebApplicationFactory CreateFactory()
    {
        return new CmsSyncWebApplicationFactory(
            _fixture.WriteConnectionString,
            _fixture.ReadConnectionString);
    }
}
