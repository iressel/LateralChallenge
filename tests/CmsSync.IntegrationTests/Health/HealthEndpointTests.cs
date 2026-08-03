using System.Diagnostics;
using System.Net;
using CmsSync.Api.Health;
using CmsSync.Infrastructure.Health;
using CmsSync.IntegrationTests.Infrastructure;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace CmsSync.IntegrationTests.Health;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "Health")]
public sealed class HealthEndpointTests
{
    private const string UnavailableConnectionString =
        "Server=127.0.0.1,1;Database=unavailable-health-sentinel;User ID=unavailable-user;" +
        "Password=<non-secret-test-sentinel>;Encrypt=false;TrustServerCertificate=true;Connect Timeout=30";

    private readonly SqlServerFixture _fixture;

    public HealthEndpointTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LivenessAndReadinessAreMinimalAnonymousAndHealthyWithSqlAvailable()
    {
        await using var factory = CreateFactory(
            _fixture.WriteConnectionString,
            _fixture.ReadConnectionString);
        using var client = CreateClient(factory);

        using var liveResponse = await client.GetAsync(
            HealthEndpointRoutes.Liveness,
            TestContext.Current.CancellationToken);
        using var readyResponse = await client.GetAsync(
            HealthEndpointRoutes.Readiness,
            TestContext.Current.CancellationToken);

        await AssertSafeHealthResponseAsync(liveResponse, HttpStatusCode.OK, "Healthy");
        await AssertSafeHealthResponseAsync(readyResponse, HttpStatusCode.OK, "Healthy");
        Assert.Empty(liveResponse.Headers.WwwAuthenticate);
        Assert.Empty(readyResponse.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task LivenessPerformsNoSqlAndRemainsHealthyWhenBothConnectionsAreUnavailable()
    {
        await using var factory = CreateFactory(
            UnavailableConnectionString,
            UnavailableConnectionString);
        using var client = CreateClient(factory);
        var startedTimestamp = Stopwatch.GetTimestamp();

        using var response = await client.GetAsync(
            HealthEndpointRoutes.Liveness,
            TestContext.Current.CancellationToken);

        await AssertSafeHealthResponseAsync(response, HttpStatusCode.OK, "Healthy");
        Assert.True(
            Stopwatch.GetElapsedTime(startedTimestamp) < TimeSpan.FromSeconds(1),
            "Liveness attempted a bounded SQL dependency probe.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadinessFailsSafelyWhenEitherRequiredSqlConnectionIsUnavailable(
        bool writeIsUnavailable)
    {
        var writeConnectionString = writeIsUnavailable
            ? UnavailableConnectionString
            : _fixture.WriteConnectionString;
        var readConnectionString = writeIsUnavailable
            ? _fixture.ReadConnectionString
            : UnavailableConnectionString;
        await using var factory = CreateFactory(writeConnectionString, readConnectionString);
        using var client = CreateClient(factory);
        var startedTimestamp = Stopwatch.GetTimestamp();

        using var response = await client.GetAsync(
            HealthEndpointRoutes.Readiness,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        await AssertSafeHealthResponseAsync(response, HttpStatusCode.ServiceUnavailable, "Unhealthy");
        Assert.True(
            Stopwatch.GetElapsedTime(startedTimestamp) < TimeSpan.FromSeconds(5),
            "Readiness exceeded its bounded dependency timeout.");
        Assert.DoesNotContain("unavailable-health-sentinel", body, StringComparison.Ordinal);
        Assert.DoesNotContain("unavailable-user", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Data.SqlClient", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectivityProbePropagatesCallerCancellation()
    {
        var healthCheck = new SqlServerConnectivityHealthCheck(
            UnavailableConnectionString,
            "readiness_read");
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            healthCheck.CheckHealthAsync(
                new HealthCheckContext(),
                cancellationSource.Token));
    }

    private static CmsSyncWebApplicationFactory CreateFactory(
        string writeConnectionString,
        string readConnectionString)
    {
        return new CmsSyncWebApplicationFactory(writeConnectionString, readConnectionString);
    }

    private static HttpClient CreateClient(CmsSyncWebApplicationFactory factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
    }

    private static async Task AssertSafeHealthResponseAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedHealth)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"{{\"status\":\"{expectedHealth}\"}}", body);
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "no-cache",
            response.Headers.Pragma.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }
}
