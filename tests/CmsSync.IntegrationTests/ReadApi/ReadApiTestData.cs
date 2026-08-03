using System.Security.Cryptography;
using System.Text;
using CmsSync.Infrastructure.Persistence;
using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.ReadApi;

internal static class ReadApiTestData
{
    private static readonly DateTime CurrentTimestamp =
        new(2026, 8, 3, 9, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime HighWatermark =
        new(2026, 8, 3, 10, 0, 0, DateTimeKind.Utc);

    public static async Task ResetActiveEntitiesAsync(ReadApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var cancellationToken = TestContext.Current.CancellationToken;

        await context.CmsEntities.ExecuteDeleteAsync(cancellationToken);
        await context.CmsDeletionTombstones.ExecuteDeleteAsync(cancellationToken);
    }

    public static async Task SeedEntitiesAsync(
        ReadApiTestHost host,
        params CmsEntity[] entities)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        context.CmsEntities.AddRange(entities);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public static async Task SeedTombstoneAsync(ReadApiTestHost host, string entityId)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();

        var existing = await context.CmsDeletionTombstones.FindAsync(
            [entityId],
            TestContext.Current.CancellationToken);

        if (existing is not null)
        {
            return;
        }

        context.CmsDeletionTombstones.Add(new CmsDeletionTombstone
        {
            EntityId = entityId,
            LastDeletedGeneration = 1,
            DeletedAtUtc = HighWatermark,
            CreatedAtUtc = HighWatermark,
            UpdatedAtUtc = HighWatermark,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public static CmsEntity CreateEntity(
        string entityId,
        string publicationStatus = "Published",
        bool administrativeDisabled = false,
        string? payload = null,
        long generation = 1,
        long latestVersion = 1)
    {
        var persistedPayload = payload ?? $"{{\"id\":\"{entityId}\"}}";

        return new CmsEntity
        {
            EntityId = entityId,
            Generation = generation,
            LatestVersion = latestVersion,
            Payload = persistedPayload,
            PayloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(persistedPayload)),
            CmsPublicationStatus = publicationStatus,
            CurrentVersionOccurredAtUtc = CurrentTimestamp,
            EntityEventHighWatermarkUtc = HighWatermark,
            AdministrativeDisabled = administrativeDisabled,
            CreatedAtUtc = CurrentTimestamp,
            UpdatedAtUtc = HighWatermark,
        };
    }
}
