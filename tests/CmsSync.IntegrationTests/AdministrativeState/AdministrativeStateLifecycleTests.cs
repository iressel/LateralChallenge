using System.Net;
using System.Text.Json;
using CmsSync.IntegrationTests.Infrastructure;
using Xunit;

namespace CmsSync.IntegrationTests.AdministrativeState;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "AdministrativeState")]
public sealed class AdministrativeStateLifecycleTests
{
    private readonly SqlServerFixture _fixture;

    public AdministrativeStateLifecycleTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CmsLifecyclePreservesThenDeletesAndResetsTheLocalOverride()
    {
        await using var host = AdministrativeStateTestHost.Create(_fixture);
        var entityId = AdministrativeStateTestData.UniqueId("lifecycle");
        await ApplyCmsEventAsync(
            host,
            AdministrativeStateTestData.Publish(
                entityId,
                version: 1,
                timestamp: "2026-08-03T10:00:00Z",
                payload: "{\"value\":1}"));
        await SetStateAsync(host, entityId, disabled: true);
        var disabled = Assert.IsType<CmsSync.Infrastructure.Persistence.Models.CmsEntity>(
            await AdministrativeStateTestData.ReadEntityAsync(host, entityId));
        var auditTimestamp = disabled.AdministrativeStateChangedAtUtc;
        var auditSubject = disabled.AdministrativeStateChangedBy;

        await ApplyCmsEventAsync(
            host,
            AdministrativeStateTestData.Publish(
                entityId,
                version: 2,
                timestamp: "2026-08-03T11:00:00Z",
                payload: "{\"value\":2}"));
        var published = Assert.IsType<CmsSync.Infrastructure.Persistence.Models.CmsEntity>(
            await AdministrativeStateTestData.ReadEntityAsync(host, entityId));
        Assert.True(published.AdministrativeDisabled);
        Assert.Equal(auditTimestamp, published.AdministrativeStateChangedAtUtc);
        Assert.Equal(auditSubject, published.AdministrativeStateChangedBy);

        await ApplyCmsEventAsync(
            host,
            AdministrativeStateTestData.Publish(
                entityId,
                version: 3,
                timestamp: "2026-08-03T12:00:00Z",
                payload: "{\"value\":3}",
                type: "unpublish"));
        var unpublished = Assert.IsType<CmsSync.Infrastructure.Persistence.Models.CmsEntity>(
            await AdministrativeStateTestData.ReadEntityAsync(host, entityId));
        Assert.Equal("Unpublished", unpublished.CmsPublicationStatus);
        Assert.True(unpublished.AdministrativeDisabled);
        Assert.Equal(auditTimestamp, unpublished.AdministrativeStateChangedAtUtc);
        Assert.Equal(auditSubject, unpublished.AdministrativeStateChangedBy);

        await ApplyCmsEventAsync(
            host,
            AdministrativeStateTestData.Delete(entityId, "2026-08-03T13:00:00Z"));
        Assert.Null(await AdministrativeStateTestData.ReadEntityAsync(host, entityId));
        Assert.Empty(await AdministrativeStateTestData.ReadRevisionsAsync(host, entityId));
        Assert.NotNull(await AdministrativeStateTestData.ReadTombstoneAsync(host, entityId));

        await ApplyCmsEventAsync(
            host,
            AdministrativeStateTestData.Publish(
                entityId,
                version: 9,
                timestamp: "2026-08-03T14:00:00Z",
                payload: "{\"value\":9}"));
        var recreated = Assert.IsType<CmsSync.Infrastructure.Persistence.Models.CmsEntity>(
            await AdministrativeStateTestData.ReadEntityAsync(host, entityId));

        Assert.Equal(2, recreated.Generation);
        Assert.Equal(9, recreated.LatestVersion);
        Assert.False(recreated.AdministrativeDisabled);
        Assert.Null(recreated.AdministrativeStateChangedAtUtc);
        Assert.Null(recreated.AdministrativeStateChangedBy);
        Assert.Single(await AdministrativeStateTestData.ReadRevisionsAsync(host, entityId));
    }

    [Fact]
    public async Task ConcurrentAdministrativeUpdatesPersistOneCompleteRequestState()
    {
        await using var host = AdministrativeStateTestHost.Create(_fixture);
        var entityId = AdministrativeStateTestData.UniqueId("admin-concurrency");
        await AdministrativeStateTestData.SeedEntityAsync(host, entityId);
        var before = Assert.IsType<CmsSync.Infrastructure.Persistence.Models.CmsEntity>(
            await AdministrativeStateTestData.ReadEntityAsync(host, entityId));
        using var firstRequest = host.CreateAdministratorPut(entityId, "{\"Disabled\":true}");
        using var secondRequest = host.CreateAdministratorPut(entityId, "{\"Disabled\":false}");
        var secondClient = host.CreateIndependentClient();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstTask = SendAfterGateAsync(host.Client, firstRequest, startGate.Task);
        var secondTask = SendAfterGateAsync(secondClient, secondRequest, startGate.Task);
        startGate.SetResult();
        var responses = await Task.WhenAll(firstTask, secondTask);

        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var responseStates = new List<bool>();

            foreach (var response in responses)
            {
                var body = await AdministrativeStateResponseAssertions.ReadJsonAsync(response);
                responseStates.Add(body.GetProperty("administrativeDisabled").GetBoolean());
            }

            var persisted = Assert.IsType<CmsSync.Infrastructure.Persistence.Models.CmsEntity>(
                await AdministrativeStateTestData.ReadEntityAsync(host, entityId));
            Assert.Contains(persisted.AdministrativeDisabled, responseStates);
            Assert.NotEqual(before.RowVersion, persisted.RowVersion);
            Assert.NotNull(persisted.AdministrativeStateChangedAtUtc);
            Assert.Equal(host.Credentials.AdministratorUsername, persisted.AdministrativeStateChangedBy);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Theory]
    [InlineData("publish", "Published")]
    [InlineData("unpublish", "Unpublished")]
    public async Task ConcurrentAdministrativeAndCmsUpdatesPreserveBothResults(
        string eventType,
        string expectedPublicationStatus)
    {
        await using var host = AdministrativeStateTestHost.Create(_fixture);
        var entityId = AdministrativeStateTestData.UniqueId("admin-cms-concurrency");
        await ApplyCmsEventAsync(
            host,
            AdministrativeStateTestData.Publish(
                entityId,
                version: 1,
                timestamp: "2026-08-03T10:00:00Z",
                payload: "{\"value\":1}"));
        await SetStateAsync(host, entityId, disabled: true);

        using var administrativeRequest = host.CreateAdministratorPut(
            entityId,
            "{\"Disabled\":false}");
        var cmsClient = host.CreateIndependentClient();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var administrativeTask = SendAfterGateAsync(
            host.Client,
            administrativeRequest,
            startGate.Task);
        var cmsTask = SendCmsAfterGateAsync(
            host,
            cmsClient,
            AdministrativeStateTestData.Publish(
                entityId,
                version: 2,
                timestamp: "2026-08-03T11:00:00Z",
                payload: "{\"value\":2}",
                type: eventType),
            startGate.Task);
        startGate.SetResult();

        using var administrativeResponse = await administrativeTask;
        using var cmsResponse = await cmsTask;
        await AssertCmsAppliedAsync(cmsResponse);
        var persisted = Assert.IsType<CmsSync.Infrastructure.Persistence.Models.CmsEntity>(
            await AdministrativeStateTestData.ReadEntityAsync(host, entityId));

        Assert.Equal(HttpStatusCode.OK, administrativeResponse.StatusCode);
        Assert.Equal(2, persisted.LatestVersion);
        Assert.Equal(expectedPublicationStatus, persisted.CmsPublicationStatus);
        Assert.False(persisted.AdministrativeDisabled);
        Assert.NotNull(persisted.AdministrativeStateChangedAtUtc);
        Assert.Equal(host.Credentials.AdministratorUsername, persisted.AdministrativeStateChangedBy);
        Assert.Equal(2, (await AdministrativeStateTestData.ReadRevisionsAsync(host, entityId)).Length);
        Assert.Equal(2, (await AdministrativeStateTestData.ReadLogsAsync(host, entityId)).Length);
    }

    [Fact]
    public async Task ConcurrentAdministrativeUpdateAndDeleteNeverResurrectTheEntity()
    {
        await using var host = AdministrativeStateTestHost.Create(_fixture);
        var entityId = AdministrativeStateTestData.UniqueId("admin-delete-concurrency");
        await ApplyCmsEventAsync(
            host,
            AdministrativeStateTestData.Publish(
                entityId,
                version: 1,
                timestamp: "2026-08-03T10:00:00Z",
                payload: "{\"value\":1}"));
        using var administrativeRequest = host.CreateAdministratorPut(
            entityId,
            "{\"Disabled\":true}");
        var cmsClient = host.CreateIndependentClient();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var administrativeTask = SendAfterGateAsync(
            host.Client,
            administrativeRequest,
            startGate.Task);
        var deleteTask = SendCmsAfterGateAsync(
            host,
            cmsClient,
            AdministrativeStateTestData.Delete(entityId, "2026-08-03T11:00:00Z"),
            startGate.Task);
        startGate.SetResult();

        using var administrativeResponse = await administrativeTask;
        using var deleteResponse = await deleteTask;
        await AssertCmsAppliedAsync(deleteResponse);

        Assert.True(
            administrativeResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound);
        Assert.Null(await AdministrativeStateTestData.ReadEntityAsync(host, entityId));
        Assert.Empty(await AdministrativeStateTestData.ReadRevisionsAsync(host, entityId));
        Assert.NotNull(await AdministrativeStateTestData.ReadTombstoneAsync(host, entityId));
        Assert.Equal(2, (await AdministrativeStateTestData.ReadLogsAsync(host, entityId)).Length);
    }

    private static async Task SetStateAsync(
        AdministrativeStateTestHost host,
        string entityId,
        bool disabled)
    {
        var disabledText = disabled ? "true" : "false";
        using var request = host.CreateAdministratorPut(
            entityId,
            $"{{\"Disabled\":{disabledText}}}");
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task ApplyCmsEventAsync(
        AdministrativeStateTestHost host,
        string eventJson)
    {
        using var response = await AdministrativeStateTestData.SendCmsEventAsync(host, eventJson);
        await AssertCmsAppliedAsync(response);
    }

    private static async Task AssertCmsAppliedAsync(HttpResponseMessage response)
    {
        var body = await AdministrativeStateResponseAssertions.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "applied",
            body.GetProperty("results")[0].GetProperty("outcome").GetString());
    }

    private static async Task<HttpResponseMessage> SendAfterGateAsync(
        HttpClient client,
        HttpRequestMessage request,
        Task startGate)
    {
        await startGate;
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> SendCmsAfterGateAsync(
        AdministrativeStateTestHost host,
        HttpClient client,
        string eventJson,
        Task startGate)
    {
        await startGate;
        return await AdministrativeStateTestData.SendCmsEventAsync(host, eventJson, client);
    }
}
