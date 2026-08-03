using System.Net;
using CmsSync.Application.Abstractions;
using CmsSync.IntegrationTests.Infrastructure;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.ReadApi;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "ReadApi")]
public sealed class ReadApiContractTests
{
    private readonly SqlServerFixture _fixture;

    public ReadApiContractTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OpaquePayloadAndRequiredFieldsAreReturnedWithoutInternalMetadataOrLogging()
    {
        using var capturedLogs = new CapturedLogProvider();
        await using var host = ReadApiTestHost.Create(_fixture, capturedLogs: capturedLogs);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);

        var entityId = $"opaque-{Guid.NewGuid():N}";
        const string sentinel = "read-api-payload-sentinel";
        const string payload =
            "{\"z\":1.0,\"nested\":{\"b\":true,\"a\":\"read-api-payload-sentinel\"},\"array\":[3,2,1]}";
        await ReadApiTestData.SeedEntitiesAsync(
            host,
            ReadApiTestData.CreateEntity(
                entityId,
                payload: payload,
                generation: 7,
                latestVersion: 11));

        using var request = host.CreateConsumerGet($"/api/entities/{entityId}");
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await ReadApiResponseAssertions.ReadJsonAsync(response);
        var propertyNames = body.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(entityId, body.GetProperty("id").GetString());
        Assert.Equal(7, body.GetProperty("generation").GetInt64());
        Assert.Equal(11, body.GetProperty("latestVersion").GetInt64());
        Assert.Equal(payload, body.GetProperty("payload").GetRawText());
        Assert.Equal("Published", body.GetProperty("cmsPublicationStatus").GetString());
        Assert.False(body.GetProperty("administrativeDisabled").GetBoolean());
        Assert.EndsWith("Z", body.GetProperty("currentVersionOccurredAtUtc").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("Z", body.GetProperty("entityEventHighWatermarkUtc").GetString(), StringComparison.Ordinal);
        Assert.Equal(
            [
                "id",
                "generation",
                "latestVersion",
                "payload",
                "cmsPublicationStatus",
                "currentVersionOccurredAtUtc",
                "entityEventHighWatermarkUtc",
                "administrativeDisabled",
            ],
            propertyNames);
        Assert.DoesNotContain("payloadHash", propertyNames);
        Assert.DoesNotContain("rowVersion", propertyNames);
        Assert.DoesNotContain("createdAtUtc", propertyNames);
        Assert.DoesNotContain("updatedAtUtc", propertyNames);
        Assert.False(capturedLogs.ContainsAny([sentinel, payload]));
    }

    [Fact]
    public async Task SuccessBadRequestUnauthorizedAndNotFoundResponsesAreNoStore()
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);
        var entityId = $"cache-{Guid.NewGuid():N}";
        await ReadApiTestData.SeedEntitiesAsync(host, ReadApiTestData.CreateEntity(entityId));

        using var successRequest = host.CreateConsumerGet($"/api/entities/{entityId}");
        using var badRequest = host.CreateConsumerGet("/api/entities?pageSize=0");
        using var unauthorizedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/entities");
        using var notFoundRequest = host.CreateConsumerGet($"/api/entities/unknown-{Guid.NewGuid():N}");
        using var successResponse = await host.Client.SendAsync(
            successRequest,
            TestContext.Current.CancellationToken);
        using var badResponse = await host.Client.SendAsync(
            badRequest,
            TestContext.Current.CancellationToken);
        using var unauthorizedResponse = await host.Client.SendAsync(
            unauthorizedRequest,
            TestContext.Current.CancellationToken);
        using var notFoundResponse = await host.Client.SendAsync(
            notFoundRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
        ReadApiResponseAssertions.AssertNoStore(successResponse);
        ReadApiResponseAssertions.AssertNoStore(badResponse);
        ReadApiResponseAssertions.AssertNoStore(unauthorizedResponse);
        ReadApiResponseAssertions.AssertNoStore(notFoundResponse);
    }

    [Fact]
    public async Task MissingAndCmsCredentialsReceiveTheConsumerBasicChallenge()
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);

        using var missingRequest = new HttpRequestMessage(HttpMethod.Get, "/api/entities");
        using var cmsRequest = host.CreateCmsGet("/api/entities");
        using var missingResponse = await host.Client.SendAsync(
            missingRequest,
            TestContext.Current.CancellationToken);
        using var cmsResponse = await host.Client.SendAsync(
            cmsRequest,
            TestContext.Current.CancellationToken);

        ReadApiResponseAssertions.AssertConsumerChallenge(missingResponse);
        ReadApiResponseAssertions.AssertConsumerChallenge(cmsResponse);
        ReadApiResponseAssertions.AssertNoStore(missingResponse);
        ReadApiResponseAssertions.AssertNoStore(cmsResponse);
    }

    [Fact]
    public async Task NormalConsumerAndAdministratorCredentialsBothReachTheReadApi()
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);

        using var consumerRequest = host.CreateConsumerGet("/api/entities");
        using var administratorRequest = host.CreateAdministratorGet("/api/entities");
        using var consumerResponse = await host.Client.SendAsync(
            consumerRequest,
            TestContext.Current.CancellationToken);
        using var administratorResponse = await host.Client.SendAsync(
            administratorRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, consumerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, administratorResponse.StatusCode);
    }

    [Fact]
    public async Task QueryFailuresReturnSafeNoStoreProblemDetails()
    {
        await using var host = ReadApiTestHost.Create(
            _fixture,
            configureServices: services =>
                services.AddScoped<ICmsEntityQueries, ThrowingCmsEntityQueries>());
        await ReadApiTestData.ResetActiveEntitiesAsync(host);

        using var request = host.CreateConsumerGet("/api/entities");
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        ReadApiResponseAssertions.AssertNoStore(response);
        Assert.Contains("ENTITY_QUERY_FAILED", body, StringComparison.Ordinal);
        Assert.DoesNotContain("CmsEntities", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stack trace", body, StringComparison.OrdinalIgnoreCase);
    }
}
