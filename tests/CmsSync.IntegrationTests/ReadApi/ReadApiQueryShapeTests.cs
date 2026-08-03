using System.Net;
using CmsSync.Application.Abstractions;
using CmsSync.Application.EntityQueries;
using CmsSync.Infrastructure.Persistence;
using CmsSync.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.ReadApi;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "ReadApi")]
public sealed class ReadApiQueryShapeTests
{
    private readonly SqlServerFixture _fixture;

    public ReadApiQueryShapeTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConsumerListFiltersOrdersLimitsAndProjectsInSql()
    {
        var interceptor = new ReadApiSqlCommandInterceptor();
        await using var host = ReadApiTestHost.Create(_fixture, interceptor);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);

        var prefix = $"query-shape-{Guid.NewGuid():N}-";
        var firstId = $"{prefix}A";
        var secondId = $"{prefix}B";
        var thirdId = $"{prefix}C";
        var fourthId = $"{prefix}D";
        await ReadApiTestData.SeedEntitiesAsync(
            host,
            ReadApiTestData.CreateEntity(firstId),
            ReadApiTestData.CreateEntity(secondId),
            ReadApiTestData.CreateEntity(thirdId),
            ReadApiTestData.CreateEntity(fourthId),
            ReadApiTestData.CreateEntity($"{prefix}E", publicationStatus: "Unpublished"),
            ReadApiTestData.CreateEntity($"{prefix}F", administrativeDisabled: true));

        using var request = host.CreateConsumerGet(
            $"/api/entities?pageSize=2&afterEntityId={Uri.EscapeDataString(firstId)}");
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await ReadApiResponseAssertions.ReadJsonAsync(response);
        var sql = Assert.Single(
            interceptor.CommandTexts,
            command => command.Contains("FROM [CmsEntities] AS", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([secondId, thirdId], ReadApiResponseAssertions.ReadItemIds(body));
        Assert.Equal(thirdId, body.GetProperty("nextCursor").GetString());
        Assert.Contains("TOP(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CmsPublicationStatus", sql, StringComparison.Ordinal);
        Assert.Contains("AdministrativeDisabled", sql, StringComparison.Ordinal);
        Assert.Contains("COLLATE Latin1_General_100_BIN2", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PayloadHash", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("AdministrativeStateChangedAtUtc", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("AdministrativeStateChangedBy", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAtUtc", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("RowVersion", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdministratorQueryRetainsAllActiveRowsWithoutConsumerVisibilityPredicate()
    {
        var interceptor = new ReadApiSqlCommandInterceptor();
        await using var host = ReadApiTestHost.Create(_fixture, interceptor);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);

        var publishedId = $"administrator-sql-a-{Guid.NewGuid():N}";
        var hiddenId = $"administrator-sql-b-{Guid.NewGuid():N}";
        await ReadApiTestData.SeedEntitiesAsync(
            host,
            ReadApiTestData.CreateEntity(publishedId),
            ReadApiTestData.CreateEntity(
                hiddenId,
                publicationStatus: "Unpublished",
                administrativeDisabled: true));

        using var request = host.CreateAdministratorGet("/api/entities?pageSize=100");
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await ReadApiResponseAssertions.ReadJsonAsync(response);
        var sql = Assert.Single(
            interceptor.CommandTexts,
            command => command.Contains("FROM [CmsEntities] AS", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([publishedId, hiddenId], ReadApiResponseAssertions.ReadItemIds(body));
        Assert.DoesNotContain("WHERE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductionQueriesLeaveTheReadContextUntrackedAndPropagateCancellation()
    {
        await using var host = ReadApiTestHost.Create(_fixture);
        await ReadApiTestData.ResetActiveEntitiesAsync(host);
        var entityId = $"no-tracking-{Guid.NewGuid():N}";
        await ReadApiTestData.SeedEntitiesAsync(host, ReadApiTestData.CreateEntity(entityId));

        using var scope = host.Services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<ICmsEntityQueries>();
        var readContext = scope.ServiceProvider.GetRequiredService<CmsReadDbContext>();
        var list = await queries.ListAsync(
            new CmsEntityListQuery(20, null, CmsEntityQueryVisibility.Consumer),
            TestContext.Current.CancellationToken);
        var detail = await queries.FindByIdAsync(
            new CmsEntityDetailQuery(entityId, CmsEntityQueryVisibility.Consumer),
            TestContext.Current.CancellationToken);

        Assert.Single(list.Items);
        Assert.NotNull(detail);
        Assert.Empty(readContext.ChangeTracker.Entries());

        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queries.ListAsync(
            new CmsEntityListQuery(20, null, CmsEntityQueryVisibility.Consumer),
            cancellationSource.Token));
        Assert.Empty(readContext.ChangeTracker.Entries());
    }
}
