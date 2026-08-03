using CmsSync.Application.Observability;
using CmsSync.Infrastructure.Persistence;
using CmsSync.IntegrationTests.Infrastructure;
using CmsSync.IntegrationTests.Persistence.SqlServer;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.Observability;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "Observability")]
public sealed class SqlTelemetryTests
{
    private readonly SqlServerFixture _fixture;

    public SqlTelemetryTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RealDeadlockRetryAndDeniedReadWriteEmitBoundedSqlMetrics()
    {
        using var metrics = new MetricCollector();
        var firstEntityId = $"telemetry-deadlock-a-{Guid.NewGuid():N}";
        var secondEntityId = $"telemetry-deadlock-b-{Guid.NewGuid():N}";
        await SeedEntityAsync(firstEntityId);
        await SeedEntityAsync(secondEntityId);
        await using var factory = new CmsSyncWebApplicationFactory(
            _fixture.WriteConnectionString,
            _fixture.ReadConnectionString);
        _ = factory.Services;

        await AssertReadConnectionWriteDeniedAsync(factory, firstEntityId);

        var gate = new AsyncDeadlockGate();
        await Task.WhenAll(
            ExecuteDeadlockingUnitAsync(factory, firstEntityId, secondEntityId, gate),
            ExecuteDeadlockingUnitAsync(factory, secondEntityId, firstEntityId, gate));

        Assert.Contains(
            metrics.Measurements,
            measurement => measurement.InstrumentName == CmsOperationalMetrics.SqlFailureInstrument &&
                           string.Equals(ReadTag(measurement, "operation"), "read_database", StringComparison.Ordinal) &&
                           string.Equals(ReadTag(measurement, "result_class"), "permanent", StringComparison.Ordinal));
        Assert.Contains(
            metrics.Measurements,
            measurement => measurement.InstrumentName == CmsOperationalMetrics.SqlDeadlockInstrument &&
                           string.Equals(ReadTag(measurement, "operation"), "write_database", StringComparison.Ordinal));
        Assert.Contains(
            metrics.Measurements,
            measurement => measurement.InstrumentName == CmsOperationalMetrics.SqlTransientRetryInstrument &&
                           string.Equals(ReadTag(measurement, "operation"), "write_database", StringComparison.Ordinal));
        Assert.DoesNotContain(
            metrics.Measurements.SelectMany(measurement => measurement.Tags.Keys),
            tag => tag.Contains("entity", StringComparison.OrdinalIgnoreCase) ||
                   tag.Contains("exception", StringComparison.OrdinalIgnoreCase));
    }

    private async Task SeedEntityAsync(string entityId)
    {
        await using var connection = new SqlConnection(_fixture.WriteConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await SqlServerTestData.InsertEntityAsync(
            connection,
            entityId,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static async Task AssertReadConnectionWriteDeniedAsync(
        CmsSyncWebApplicationFactory factory,
        string entityId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsReadDbContext>();

        await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CmsEntities] SET [UpdatedAtUtc] = SYSUTCDATETIME() WHERE [EntityId] = {entityId}",
                TestContext.Current.CancellationToken));
    }

    private static async Task ExecuteDeadlockingUnitAsync(
        CmsSyncWebApplicationFactory factory,
        string firstEntityId,
        string secondEntityId,
        AsyncDeadlockGate gate)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var strategy = context.Database.CreateExecutionStrategy();
        var attempt = 0;

        await strategy.ExecuteAsync(async cancellationToken =>
        {
            var currentAttempt = Interlocked.Increment(ref attempt);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await UpdateEntityAsync(context, firstEntityId, cancellationToken);

            if (currentAttempt == 1)
            {
                await gate.SignalAndWaitAsync(cancellationToken);
            }

            await UpdateEntityAsync(context, secondEntityId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }, TestContext.Current.CancellationToken);
    }

    private static Task<int> UpdateEntityAsync(
        CmsWriteDbContext context,
        string entityId,
        CancellationToken cancellationToken)
    {
        return context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [CmsEntities] SET [UpdatedAtUtc] = SYSUTCDATETIME() WHERE [EntityId] = {entityId}",
            cancellationToken);
    }

    private static string? ReadTag(MetricMeasurement measurement, string name)
    {
        return measurement.Tags.TryGetValue(name, out var value)
            ? value?.ToString()
            : null;
    }
}
