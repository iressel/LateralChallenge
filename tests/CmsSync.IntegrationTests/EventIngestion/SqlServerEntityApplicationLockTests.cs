using System.Data;
using CmsSync.Application.EventIngestion;
using CmsSync.Infrastructure.Persistence.EventProcessing;
using CmsSync.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Xunit;

namespace CmsSync.IntegrationTests.EventIngestion;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "EventProcessing")]
public sealed class SqlServerEntityApplicationLockTests
{
    private readonly SqlServerFixture _fixture;

    public SqlServerEntityApplicationLockTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void ResourceIsStableCaseSensitiveFixedLengthAndPayloadFree()
    {
        const string entityId = "Lock-Proof-Secret-Entity";

        var first = SqlServerEntityApplicationLock.CreateResource(entityId);
        var repeated = SqlServerEntityApplicationLock.CreateResource(entityId);
        var differentCase = SqlServerEntityApplicationLock.CreateResource(
            entityId.ToUpperInvariant());

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, differentCase);
        Assert.Equal("CmsSync:Entity:".Length + 64, first.Length);
        Assert.StartsWith("CmsSync:Entity:", first, StringComparison.Ordinal);
        Assert.DoesNotContain(entityId, first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransactionOwnedExclusiveLockTimesOutCompetitorAndRollbackReleasesResource()
    {
        var entityId = EventProcessingTestData.UniqueId("lock-owner");
        var holder = new SqlServerEntityApplicationLock(TimeSpan.FromSeconds(1));
        var competitor = new SqlServerEntityApplicationLock(TimeSpan.FromMilliseconds(100));

        await using var firstConnection = new SqlConnection(_fixture.WriteConnectionString);
        await firstConnection.OpenAsync(TestContext.Current.CancellationToken);
        await using var firstTransaction = await firstConnection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            TestContext.Current.CancellationToken);
        await holder.AcquireAsync(
            firstConnection,
            firstTransaction,
            entityId,
            TestContext.Current.CancellationToken);

        await using var secondConnection = new SqlConnection(_fixture.WriteConnectionString);
        await secondConnection.OpenAsync(TestContext.Current.CancellationToken);
        await using var blockedTransaction = await secondConnection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<EventProcessingDependencyUnavailableException>(() =>
            competitor.AcquireAsync(
                secondConnection,
                blockedTransaction,
                entityId,
                TestContext.Current.CancellationToken));
        await blockedTransaction.RollbackAsync(TestContext.Current.CancellationToken);
        await firstTransaction.RollbackAsync(TestContext.Current.CancellationToken);

        await using var releasedTransaction = await secondConnection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            TestContext.Current.CancellationToken);
        await competitor.AcquireAsync(
            secondConnection,
            releasedTransaction,
            entityId,
            TestContext.Current.CancellationToken);
        await releasedTransaction.CommitAsync(TestContext.Current.CancellationToken);
    }
}
