using System.Net;
using CmsSync.IntegrationTests.Infrastructure;
using Xunit;

namespace CmsSync.IntegrationTests.ReadApi;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "ReadApi")]
public sealed class ReadApiVisibilityTests
{
    private readonly SqlServerFixture _fixture;

    public ReadApiVisibilityTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task NormalConsumerListAndDetailApplyTheFourStateVisibilityMatrix()
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);

        var visibleId = $"visibility-a-{Guid.NewGuid():N}";
        var disabledId = $"visibility-b-{Guid.NewGuid():N}";
        var unpublishedId = $"visibility-c-{Guid.NewGuid():N}";
        var unpublishedDisabledId = $"visibility-d-{Guid.NewGuid():N}";
        await ReadApiTestData.SeedEntitiesAsync(
            host,
            ReadApiTestData.CreateEntity(visibleId),
            ReadApiTestData.CreateEntity(disabledId, administrativeDisabled: true),
            ReadApiTestData.CreateEntity(unpublishedId, publicationStatus: "Unpublished"),
            ReadApiTestData.CreateEntity(
                unpublishedDisabledId,
                publicationStatus: "Unpublished",
                administrativeDisabled: true));

        using var listRequest = host.CreateConsumerGet("/api/entities?pageSize=100");
        using var listResponse = await host.Client.SendAsync(
            listRequest,
            TestContext.Current.CancellationToken);
        var listBody = await ReadApiResponseAssertions.ReadJsonAsync(listResponse);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal([visibleId], ReadApiResponseAssertions.ReadItemIds(listBody));

        await AssertDetailStatusAsync(host, visibleId, HttpStatusCode.OK);
        await AssertDetailStatusAsync(host, disabledId, HttpStatusCode.NotFound);
        await AssertDetailStatusAsync(host, unpublishedId, HttpStatusCode.NotFound);
        await AssertDetailStatusAsync(host, unpublishedDisabledId, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdministratorListAndDetailReturnEveryActiveStateWithStateIndicators()
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);

        var expectedStates = new Dictionary<string, (string PublicationStatus, bool Disabled)>
        {
            [$"administrator-a-{Guid.NewGuid():N}"] = ("Published", false),
            [$"administrator-b-{Guid.NewGuid():N}"] = ("Published", true),
            [$"administrator-c-{Guid.NewGuid():N}"] = ("Unpublished", false),
            [$"administrator-d-{Guid.NewGuid():N}"] = ("Unpublished", true),
        };
        await ReadApiTestData.SeedEntitiesAsync(
            host,
            expectedStates.Select(state => ReadApiTestData.CreateEntity(
                state.Key,
                state.Value.PublicationStatus,
                state.Value.Disabled)).ToArray());

        using var listRequest = host.CreateAdministratorGet("/api/entities?pageSize=100");
        using var listResponse = await host.Client.SendAsync(
            listRequest,
            TestContext.Current.CancellationToken);
        var listBody = await ReadApiResponseAssertions.ReadJsonAsync(listResponse);
        var items = listBody.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(expectedStates.Count, items.Length);

        foreach (var item in items)
        {
            var entityId = item.GetProperty("id").GetString()!;
            var expected = expectedStates[entityId];
            Assert.Equal(expected.PublicationStatus, item.GetProperty("cmsPublicationStatus").GetString());
            Assert.Equal(expected.Disabled, item.GetProperty("administrativeDisabled").GetBoolean());

            using var detailRequest = host.CreateAdministratorGet($"/api/entities/{entityId}");
            using var detailResponse = await host.Client.SendAsync(
                detailRequest,
                TestContext.Current.CancellationToken);
            var detailBody = await ReadApiResponseAssertions.ReadJsonAsync(detailResponse);

            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            Assert.Equal(expected.PublicationStatus, detailBody.GetProperty("cmsPublicationStatus").GetString());
            Assert.Equal(expected.Disabled, detailBody.GetProperty("administrativeDisabled").GetBoolean());
        }
    }

    [Fact]
    public async Task DeletedEntitiesNeverAppearForEitherConsumerRole()
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);

        var deletedId = $"deleted-{Guid.NewGuid():N}";
        await ReadApiTestData.SeedTombstoneAsync(host, deletedId);

        using var consumerListRequest = host.CreateConsumerGet("/api/entities?pageSize=100");
        using var administratorListRequest = host.CreateAdministratorGet("/api/entities?pageSize=100");
        using var consumerListResponse = await host.Client.SendAsync(
            consumerListRequest,
            TestContext.Current.CancellationToken);
        using var administratorListResponse = await host.Client.SendAsync(
            administratorListRequest,
            TestContext.Current.CancellationToken);

        Assert.Empty(ReadApiResponseAssertions.ReadItemIds(
            await ReadApiResponseAssertions.ReadJsonAsync(consumerListResponse)));
        Assert.Empty(ReadApiResponseAssertions.ReadItemIds(
            await ReadApiResponseAssertions.ReadJsonAsync(administratorListResponse)));

        using var consumerDetailRequest = host.CreateConsumerGet($"/api/entities/{deletedId}");
        using var administratorDetailRequest = host.CreateAdministratorGet($"/api/entities/{deletedId}");
        using var consumerDetailResponse = await host.Client.SendAsync(
            consumerDetailRequest,
            TestContext.Current.CancellationToken);
        using var administratorDetailResponse = await host.Client.SendAsync(
            administratorDetailRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, consumerDetailResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, administratorDetailResponse.StatusCode);
    }

    [Fact]
    public async Task HiddenDeletedAndUnknownDetailsUseIndistinguishableNotFoundResponses()
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);

        var hiddenId = $"hidden-{Guid.NewGuid():N}";
        var deletedId = $"deleted-{Guid.NewGuid():N}";
        var unknownId = $"unknown-{Guid.NewGuid():N}";
        await ReadApiTestData.SeedEntitiesAsync(
            host,
            ReadApiTestData.CreateEntity(hiddenId, publicationStatus: "Unpublished"));
        await ReadApiTestData.SeedTombstoneAsync(host, deletedId);

        var problemDetails = new List<Dictionary<string, string>>();

        foreach (var entityId in new[] { hiddenId, deletedId, unknownId })
        {
            using var request = host.CreateConsumerGet($"/api/entities/{entityId}");
            using var response = await host.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken);
            var jsonBody = await ReadApiResponseAssertions.ReadJsonAsync(response);
            var body = jsonBody.GetRawText();

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            ReadApiResponseAssertions.AssertNoStore(response);
            Assert.DoesNotContain(entityId, body, StringComparison.Ordinal);
            problemDetails.Add(jsonBody
                .EnumerateObject()
                .Where(property => !string.Equals(
                    property.Name,
                    "traceId",
                    StringComparison.Ordinal))
                .ToDictionary(
                    property => property.Name,
                    property => property.Value.ToString(),
                    StringComparer.Ordinal));
        }

        Assert.All(problemDetails, details => Assert.Equal(problemDetails[0], details));
    }

    [Fact]
    public async Task DetailLookupPreservesExactCaseSensitiveEntityIdentifiers()
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);

        var uppercaseId = $"Case-{Guid.NewGuid():N}";
        var lowercaseId = $"case-{uppercaseId[5..]}";
        await ReadApiTestData.SeedEntitiesAsync(host, ReadApiTestData.CreateEntity(uppercaseId));
        await ReadApiTestData.SeedEntitiesAsync(host, ReadApiTestData.CreateEntity(lowercaseId));

        await AssertDetailIdAsync(host, uppercaseId);
        await AssertDetailIdAsync(host, lowercaseId);

        var unmatchedId = $"CASE-{uppercaseId[5..]}";
        using var unmatchedRequest = host.CreateConsumerGet($"/api/entities/{unmatchedId}");
        using var unmatchedResponse = await host.Client.SendAsync(
            unmatchedRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, unmatchedResponse.StatusCode);
    }

    private static async Task AssertDetailStatusAsync(
        ReadApiTestHost host,
        string entityId,
        HttpStatusCode expectedStatus)
    {
        using var request = host.CreateConsumerGet($"/api/entities/{entityId}");
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    private static async Task AssertDetailIdAsync(ReadApiTestHost host, string expectedEntityId)
    {
        using var request = host.CreateConsumerGet($"/api/entities/{expectedEntityId}");
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await ReadApiResponseAssertions.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedEntityId, body.GetProperty("id").GetString());
    }
}
