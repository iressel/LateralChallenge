using System.Net;
using CmsSync.Infrastructure.Persistence;
using CmsSync.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.AdministrativeState;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "AdministrativeState")]
public sealed class AdministrativeStatePersistenceTests
{
    private readonly SqlServerFixture _fixture;

    public AdministrativeStatePersistenceTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MutationChangesOnlyLocalAdministrativeFields()
    {
        await using var host = AdministrativeStateTestHost.Create(_fixture);
        var entityId = AdministrativeStateTestData.UniqueId("local-only");
        using var publishResponse = await AdministrativeStateTestData.SendCmsEventAsync(
            host,
            AdministrativeStateTestData.Publish(
                entityId,
                version: 7,
                timestamp: "2026-08-03T10:00:00Z",
                payload: "{\"cms\":\"owned\"}"));
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        var before = Assert.IsType<CmsSync.Infrastructure.Persistence.Models.CmsEntity>(
            await AdministrativeStateTestData.ReadEntityAsync(host, entityId));
        var revisionsBefore = await AdministrativeStateTestData.ReadRevisionsAsync(host, entityId);
        var logsBefore = await AdministrativeStateTestData.ReadLogsAsync(host, entityId);
        using var request = host.CreateAdministratorPut(entityId, "{\"Disabled\":true}");

        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var after = Assert.IsType<CmsSync.Infrastructure.Persistence.Models.CmsEntity>(
            await AdministrativeStateTestData.ReadEntityAsync(host, entityId));
        var revisionsAfter = await AdministrativeStateTestData.ReadRevisionsAsync(host, entityId);
        var logsAfter = await AdministrativeStateTestData.ReadLogsAsync(host, entityId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before.EntityId, after.EntityId);
        Assert.Equal(before.Generation, after.Generation);
        Assert.Equal(before.LatestVersion, after.LatestVersion);
        Assert.Equal(before.Payload, after.Payload);
        Assert.Equal(before.PayloadHash, after.PayloadHash);
        Assert.Equal(before.CmsPublicationStatus, after.CmsPublicationStatus);
        Assert.Equal(before.CurrentVersionOccurredAtUtc, after.CurrentVersionOccurredAtUtc);
        Assert.Equal(before.EntityEventHighWatermarkUtc, after.EntityEventHighWatermarkUtc);
        Assert.Equal(before.CreatedAtUtc, after.CreatedAtUtc);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
        Assert.False(before.AdministrativeDisabled);
        Assert.True(after.AdministrativeDisabled);
        Assert.NotNull(after.AdministrativeStateChangedAtUtc);
        Assert.Equal(host.Credentials.AdministratorUsername, after.AdministrativeStateChangedBy);
        Assert.NotEqual(before.RowVersion, after.RowVersion);
        Assert.Equivalent(revisionsBefore, revisionsAfter, strict: true);
        Assert.Equivalent(logsBefore, logsAfter, strict: true);
    }

    [Fact]
    public async Task RowVersionConcurrencyFailureReloadsAndRetriesSuccessfully()
    {
        var interceptor = new AdministrativeConcurrencyFailureInterceptor(failuresToInject: 1);
        await using var host = CreateHost(interceptor);
        var entityId = AdministrativeStateTestData.UniqueId("retry");
        await AdministrativeStateTestData.SeedEntityAsync(host, entityId);
        using var request = host.CreateAdministratorPut(entityId, "{\"Disabled\":true}");

        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var persisted = Assert.IsType<CmsSync.Infrastructure.Persistence.Models.CmsEntity>(
            await AdministrativeStateTestData.ReadEntityAsync(host, entityId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, interceptor.InjectedFailures);
        Assert.True(persisted.AdministrativeDisabled);
        Assert.NotNull(persisted.AdministrativeStateChangedAtUtc);
        Assert.Equal(host.Credentials.AdministratorUsername, persisted.AdministrativeStateChangedBy);
    }

    [Fact]
    public async Task RowVersionRetriesAreBoundedAndReturnSafeServiceUnavailable()
    {
        var interceptor = new AdministrativeConcurrencyFailureInterceptor(failuresToInject: 3);
        await using var host = CreateHost(interceptor);
        var entityId = AdministrativeStateTestData.UniqueId("retry-exhausted");
        await AdministrativeStateTestData.SeedEntityAsync(host, entityId);
        var before = Assert.IsType<CmsSync.Infrastructure.Persistence.Models.CmsEntity>(
            await AdministrativeStateTestData.ReadEntityAsync(host, entityId));
        using var request = host.CreateAdministratorPut(entityId, "{\"Disabled\":true}");

        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var after = Assert.IsType<CmsSync.Infrastructure.Persistence.Models.CmsEntity>(
            await AdministrativeStateTestData.ReadEntityAsync(host, entityId));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, interceptor.InjectedFailures);
        Assert.Contains("ADMINISTRATIVE_STATE_UNAVAILABLE", body, StringComparison.Ordinal);
        Assert.DoesNotContain("DbUpdateConcurrencyException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("RowVersion", body, StringComparison.Ordinal);
        Assert.False(after.AdministrativeDisabled);
        Assert.Null(after.AdministrativeStateChangedAtUtc);
        Assert.Null(after.AdministrativeStateChangedBy);
        Assert.Equal(before.RowVersion, after.RowVersion);
        AdministrativeStateResponseAssertions.AssertNoStore(response);
    }

    private AdministrativeStateTestHost CreateHost(
        AdministrativeConcurrencyFailureInterceptor interceptor)
    {
        return AdministrativeStateTestHost.Create(
            _fixture,
            services =>
            {
                services.AddDbContext<CmsWriteDbContext>(options =>
                    options.AddInterceptors(interceptor));
            });
    }
}
