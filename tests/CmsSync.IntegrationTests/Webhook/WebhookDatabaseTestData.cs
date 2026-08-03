using CmsSync.Infrastructure.Persistence;
using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.Webhook;

internal static class WebhookDatabaseTestData
{
    public static async Task<CmsEntity?> ReadEntityAsync(
        WebhookTestHost host,
        string entityId)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEntities.AsNoTracking().SingleOrDefaultAsync(
            entity => entity.EntityId == entityId,
            TestContext.Current.CancellationToken);
    }

    public static async Task<CmsEntityRevision?> ReadRevisionAsync(
        WebhookTestHost host,
        string entityId,
        long generation,
        long version)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEntityRevisions.AsNoTracking().SingleOrDefaultAsync(
            revision =>
                revision.EntityId == entityId &&
                revision.Generation == generation &&
                revision.Version == version,
            TestContext.Current.CancellationToken);
    }

    public static async Task<CmsDeletionTombstone?> ReadTombstoneAsync(
        WebhookTestHost host,
        string entityId)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsDeletionTombstones.AsNoTracking().SingleOrDefaultAsync(
            tombstone => tombstone.EntityId == entityId,
            TestContext.Current.CancellationToken);
    }

    public static async Task<CmsEventProcessingLog[]> ReadLogsAsync(
        WebhookTestHost host,
        string entityId)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEventProcessingLogs.AsNoTracking()
            .Where(log => log.EntityId == entityId)
            .OrderBy(log => log.ProcessingLogId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
    }

    public static async Task<int> CountRevisionsAsync(
        WebhookTestHost host,
        string entityId)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEntityRevisions.CountAsync(
            revision => revision.EntityId == entityId,
            TestContext.Current.CancellationToken);
    }
}
