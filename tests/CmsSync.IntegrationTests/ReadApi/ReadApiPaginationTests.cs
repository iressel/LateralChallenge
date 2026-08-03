using System.Net;
using CmsSync.IntegrationTests.Infrastructure;
using Xunit;

namespace CmsSync.IntegrationTests.ReadApi;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "ReadApi")]
public sealed class ReadApiPaginationTests
{
    private readonly SqlServerFixture _fixture;

    public ReadApiPaginationTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DefaultPageSizeIsTwentyAndFinalPageOmitsNextCursor()
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);
        var expectedIds = await SeedOrderedEntitiesAsync(host, 21);

        var firstPage = await GetPageAsync(host, "/api/entities");

        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        Assert.Equal(20, firstPage.PageSize);
        Assert.Equal(expectedIds[..20], firstPage.Ids);
        Assert.Equal(expectedIds[19], firstPage.NextCursor);

        var finalPage = await GetPageAsync(
            host,
            $"/api/entities?afterEntityId={Uri.EscapeDataString(firstPage.NextCursor!)}");

        Assert.Equal([expectedIds[20]], finalPage.Ids);
        Assert.Null(finalPage.NextCursor);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task MinimumAndMaximumPageSizesAreAccepted(int pageSize)
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);
        var expectedIds = await SeedOrderedEntitiesAsync(host, pageSize + 1);

        var page = await GetPageAsync(host, $"/api/entities?pageSize={pageSize}");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(pageSize, page.PageSize);
        Assert.Equal(expectedIds[..pageSize], page.Ids);
        Assert.Equal(expectedIds[pageSize - 1], page.NextCursor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("+1")]
    [InlineData("2147483648")]
    public async Task InvalidPageSizesReturnBadRequest(string suppliedPageSize)
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);
        var requestUri = $"/api/entities?pageSize={Uri.EscapeDataString(suppliedPageSize)}";

        using var request = host.CreateConsumerGet(requestUri);
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await ReadApiResponseAssertions.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_PAGE_SIZE", body.GetProperty("code").GetString());
        Assert.DoesNotContain("Sql", body.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyListReturnsNoItemsAndNoNextCursor()
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);

        var page = await GetPageAsync(host, "/api/entities?pageSize=20");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Empty(page.Ids);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task CursorTraversalReturnsFirstMiddleAndFinalPagesWithoutDuplicatesOrGaps()
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);
        var expectedIds = await SeedOrderedEntitiesAsync(host, 7);

        var firstPage = await GetPageAsync(host, "/api/entities?pageSize=3");
        var middlePage = await GetPageAsync(
            host,
            $"/api/entities?pageSize=3&afterEntityId={Uri.EscapeDataString(firstPage.NextCursor!)}");
        var finalPage = await GetPageAsync(
            host,
            $"/api/entities?pageSize=3&afterEntityId={Uri.EscapeDataString(middlePage.NextCursor!)}");
        var traversedIds = firstPage.Ids.Concat(middlePage.Ids).Concat(finalPage.Ids).ToArray();

        Assert.Equal(expectedIds[..3], firstPage.Ids);
        Assert.Equal(expectedIds[2], firstPage.NextCursor);
        Assert.Equal(expectedIds[3..6], middlePage.Ids);
        Assert.Equal(expectedIds[5], middlePage.NextCursor);
        Assert.Equal([expectedIds[6]], finalPage.Ids);
        Assert.Null(finalPage.NextCursor);
        Assert.Equal(expectedIds, traversedIds);
        Assert.Equal(traversedIds.Length, traversedIds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task CaseDistinctIdentifiersRemainSeparateAcrossPageBoundaries()
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);
        var prefix = $"case-boundary-{Guid.NewGuid():N}-";
        var expectedIds = new[]
        {
            $"{prefix}A",
            $"{prefix}a",
            $"{prefix}B",
            $"{prefix}b",
        }
        .Order(StringComparer.Ordinal)
        .ToArray();
        foreach (var entityId in expectedIds)
        {
            await ReadApiTestData.SeedEntitiesAsync(host, ReadApiTestData.CreateEntity(entityId));
        }

        var traversedIds = new List<string>();
        string? cursor = null;

        do
        {
            var requestUri = cursor is null
                ? "/api/entities?pageSize=1"
                : $"/api/entities?pageSize=1&afterEntityId={Uri.EscapeDataString(cursor)}";
            var page = await GetPageAsync(host, requestUri);
            traversedIds.AddRange(page.Ids);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(expectedIds, traversedIds);
    }

    [Fact]
    public async Task RepeatingTheSameCursorQueryReturnsStableResults()
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);
        var expectedIds = await SeedOrderedEntitiesAsync(host, 8);
        var requestUri =
            $"/api/entities?pageSize=3&afterEntityId={Uri.EscapeDataString(expectedIds[1])}";

        var firstExecution = await GetPageAsync(host, requestUri);
        var secondExecution = await GetPageAsync(host, requestUri);

        Assert.Equal(expectedIds[2..5], firstExecution.Ids);
        Assert.Equal(firstExecution.Ids, secondExecution.Ids);
        Assert.Equal(firstExecution.NextCursor, secondExecution.NextCursor);
    }

    private static async Task<string[]> SeedOrderedEntitiesAsync(
        ReadApiTestHost host,
        int count)
    {
        var prefix = $"pagination-{Guid.NewGuid():N}-";
        var entityIds = Enumerable.Range(0, count)
            .Select(index => $"{prefix}{index:D3}")
            .ToArray();
        await ReadApiTestData.SeedEntitiesAsync(
            host,
            entityIds.Select(entityId => ReadApiTestData.CreateEntity(entityId)).ToArray());

        return entityIds;
    }

    private static async Task<ReadApiPageResult> GetPageAsync(
        ReadApiTestHost host,
        string requestUri)
    {
        using var request = host.CreateConsumerGet(requestUri);
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await ReadApiResponseAssertions.ReadJsonAsync(response);
        var nextCursor = body.TryGetProperty("nextCursor", out var cursorProperty)
            ? cursorProperty.GetString()
            : null;

        return new ReadApiPageResult(
            response.StatusCode,
            body.GetProperty("pageSize").GetInt32(),
            ReadApiResponseAssertions.ReadItemIds(body),
            nextCursor);
    }
}
