using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CmsSync.Infrastructure.Persistence;
using CmsSync.Infrastructure.Persistence.Models;
using CmsSync.IntegrationTests.EventIngestion;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.AdministrativeState;

internal static class AdministrativeStateTestData
{
    private static readonly DateTime InitialTimestamp =
        new(2026, 8, 3, 10, 0, 0, DateTimeKind.Utc);

    public static string UniqueId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    public static async Task SeedEntityAsync(
        AdministrativeStateTestHost host,
        string entityId,
        bool disabled = false)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        const string payload = "{\"value\":1}";
        context.CmsEntities.Add(new CmsEntity
        {
            EntityId = entityId,
            Generation = 1,
            LatestVersion = 1,
            Payload = payload,
            PayloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(payload)),
            CmsPublicationStatus = "Published",
            CurrentVersionOccurredAtUtc = InitialTimestamp,
            EntityEventHighWatermarkUtc = InitialTimestamp,
            AdministrativeDisabled = disabled,
            CreatedAtUtc = InitialTimestamp,
            UpdatedAtUtc = InitialTimestamp,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public static async Task<CmsEntity?> ReadEntityAsync(
        AdministrativeStateTestHost host,
        string entityId)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEntities.AsNoTracking().SingleOrDefaultAsync(
            entity => entity.EntityId == entityId,
            TestContext.Current.CancellationToken);
    }

    public static async Task<CmsEntityRevision[]> ReadRevisionsAsync(
        AdministrativeStateTestHost host,
        string entityId)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEntityRevisions.AsNoTracking()
            .Where(revision => revision.EntityId == entityId)
            .OrderBy(revision => revision.Generation)
            .ThenBy(revision => revision.Version)
            .ToArrayAsync(TestContext.Current.CancellationToken);
    }

    public static async Task<CmsEventProcessingLog[]> ReadLogsAsync(
        AdministrativeStateTestHost host,
        string entityId)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEventProcessingLogs.AsNoTracking()
            .Where(log => log.EntityId == entityId)
            .OrderBy(log => log.ProcessingLogId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
    }

    public static async Task<CmsDeletionTombstone?> ReadTombstoneAsync(
        AdministrativeStateTestHost host,
        string entityId)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsDeletionTombstones.AsNoTracking().SingleOrDefaultAsync(
            tombstone => tombstone.EntityId == entityId,
            TestContext.Current.CancellationToken);
    }

    public static async Task<HttpResponseMessage> SendCmsEventAsync(
        AdministrativeStateTestHost host,
        string eventJson,
        HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/cms/events")
        {
            Content = new StringContent($"[{eventJson}]", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            AuthenticationRequestFactory.CreateBasicParameter(
                host.Credentials.CmsUsername,
                host.Credentials.CmsPassword));

        try
        {
            return await (client ?? host.Client).SendAsync(
                request,
                TestContext.Current.CancellationToken);
        }
        finally
        {
            request.Dispose();
        }
    }

    public static string Publish(
        string entityId,
        long version,
        string timestamp,
        string payload,
        string type = "publish")
    {
        return EventProcessingTestData.Publish(
            entityId,
            version,
            UniqueId("event"),
            timestamp,
            payload,
            type);
    }

    public static string Delete(string entityId, string timestamp)
    {
        return EventProcessingTestData.Delete(entityId, UniqueId("event"), timestamp);
    }
}
