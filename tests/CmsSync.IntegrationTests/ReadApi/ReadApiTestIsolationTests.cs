using CmsSync.Infrastructure.Persistence;
using CmsSync.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.ReadApi;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "ReadApi")]
public sealed class ReadApiTestIsolationTests
{
    private readonly SqlServerFixture _fixture;

    public ReadApiTestIsolationTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ResetRemovesPreviouslySeededTombstone()
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);
        var entityId = $"reset-tombstone-{Guid.NewGuid():N}";
        await ReadApiTestData.SeedTombstoneAsync(host, entityId);
        Assert.True(await TombstoneExistsAsync(host, entityId));

        await ReadApiTestData.ResetActiveEntitiesAsync(host);

        Assert.False(await TombstoneExistsAsync(host, entityId));
    }

    private static async Task<bool> TombstoneExistsAsync(
        ReadApiTestHost host,
        string entityId)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsDeletionTombstones.AnyAsync(
            tombstone => tombstone.EntityId == entityId,
            TestContext.Current.CancellationToken);
    }
}
