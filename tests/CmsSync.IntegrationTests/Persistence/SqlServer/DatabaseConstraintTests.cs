using CmsSync.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Xunit;

namespace CmsSync.IntegrationTests.Persistence.SqlServer;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "SqlServer")]
public sealed class DatabaseConstraintTests
{
    private readonly SqlServerFixture _fixture;

    public DatabaseConstraintTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EntityIdentityIsUniqueAndCaseSensitiveAndRowVersionIsGenerated()
    {
        await using var connection = await OpenWriteConnectionAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var firstId = $"CaseSensitiveId-{suffix}";
        var secondId = $"casesensitiveid-{suffix}";

        await SqlServerTestData.InsertEntityAsync(
            connection,
            firstId,
            cancellationToken: TestContext.Current.CancellationToken);
        await AssertSqlErrorAsync(
            2627,
            () => SqlServerTestData.InsertEntityAsync(
                connection,
                firstId,
                cancellationToken: TestContext.Current.CancellationToken));
        await SqlServerTestData.InsertEntityAsync(
            connection,
            secondId,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT [RowVersion] FROM [CmsEntities] WHERE [EntityId] = @entityId";
        command.Parameters.Add("@entityId", System.Data.SqlDbType.NVarChar, 200).Value = firstId;
        var rowVersion = Assert.IsType<byte[]>(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        Assert.Equal(8, rowVersion.Length);
    }

    [Fact]
    public async Task EntityRangeJsonStatusTimestampAndAuditChecksRejectInvalidRows()
    {
        await using var connection = await OpenWriteConnectionAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        await AssertSqlErrorAsync(547, () => SqlServerTestData.InsertEntityAsync(
            connection,
            UniqueId("generation-zero"),
            generation: 0,
            cancellationToken: cancellationToken));
        await AssertSqlErrorAsync(547, () => SqlServerTestData.InsertEntityAsync(
            connection,
            UniqueId("version-zero"),
            latestVersion: 0,
            cancellationToken: cancellationToken));
        await AssertSqlErrorAsync(547, () => SqlServerTestData.InsertEntityAsync(
            connection,
            UniqueId("invalid-json"),
            payload: "not-json",
            cancellationToken: cancellationToken));
        await AssertSqlErrorAsync(547, () => SqlServerTestData.InsertEntityAsync(
            connection,
            UniqueId("array-json"),
            payload: "[1,2,3]",
            cancellationToken: cancellationToken));
        await AssertSqlErrorAsync(547, () => SqlServerTestData.InsertEntityAsync(
            connection,
            UniqueId("status-case"),
            publicationStatus: "published",
            cancellationToken: cancellationToken));
        await AssertSqlErrorAsync(547, () => SqlServerTestData.InsertEntityAsync(
            connection,
            UniqueId("watermark"),
            currentVersionOccurredAtUtc: new DateTime(2026, 8, 2, 12, 0, 1, DateTimeKind.Utc),
            entityEventHighWatermarkUtc: new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc),
            cancellationToken: cancellationToken));
        await AssertSqlErrorAsync(547, () => SqlServerTestData.InsertEntityAsync(
            connection,
            UniqueId("audit"),
            administrativeDisabled: true,
            administrativeStateChangedAtUtc: new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc),
            administrativeStateChangedBy: null,
            cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task RevisionIdentityAndJsonConstraintsAreEnforced()
    {
        await using var connection = await OpenWriteConnectionAsync();
        var entityId = UniqueId("revision");

        await SqlServerTestData.InsertRevisionAsync(
            connection,
            entityId,
            cancellationToken: TestContext.Current.CancellationToken);
        await AssertSqlErrorAsync(
            2627,
            () => SqlServerTestData.InsertRevisionAsync(
                connection,
                entityId,
                cancellationToken: TestContext.Current.CancellationToken));
        await AssertSqlErrorAsync(
            547,
            () => SqlServerTestData.InsertRevisionAsync(
                connection,
                UniqueId("revision-json"),
                payload: "[]",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TombstoneAllowsGenerationZeroButRejectsNegativeGeneration()
    {
        await using var connection = await OpenWriteConnectionAsync();

        await SqlServerTestData.InsertTombstoneAsync(
            connection,
            UniqueId("tombstone-zero"),
            0,
            TestContext.Current.CancellationToken);
        await AssertSqlErrorAsync(
            547,
            () => SqlServerTestData.InsertTombstoneAsync(
                connection,
                UniqueId("tombstone-negative"),
                -1,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProcessingLogBatchAndIdempotencyOwnershipAreEnforced()
    {
        await using var connection = await OpenWriteConnectionAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var batchId = Guid.NewGuid();
        var ownerKey = $"external:{Guid.NewGuid():N}";

        await SqlServerTestData.InsertProcessingLogAsync(
            connection,
            batchId: batchId,
            sequence: 7,
            cancellationToken: cancellationToken);
        await AssertSqlErrorAsync(
            2601,
            () => SqlServerTestData.InsertProcessingLogAsync(
                connection,
                batchId: batchId,
                sequence: 7,
                cancellationToken: cancellationToken));
        await AssertSqlErrorAsync(
            547,
            () => SqlServerTestData.InsertProcessingLogAsync(
                connection,
                sequence: -1,
                cancellationToken: cancellationToken));
        await AssertSqlErrorAsync(
            547,
            () => SqlServerTestData.InsertProcessingLogAsync(
                connection,
                idempotencyKey: null,
                ownsIdempotencyKey: true,
                cancellationToken: cancellationToken));

        var ownerId = await SqlServerTestData.InsertProcessingLogAsync(
            connection,
            idempotencyKey: ownerKey,
            ownsIdempotencyKey: true,
            cancellationToken: cancellationToken);
        await AssertSqlErrorAsync(
            2601,
            () => SqlServerTestData.InsertProcessingLogAsync(
                connection,
                idempotencyKey: ownerKey,
                ownsIdempotencyKey: true,
                cancellationToken: cancellationToken));

        _ = await SqlServerTestData.InsertProcessingLogAsync(
            connection,
            idempotencyKey: ownerKey,
            ownsIdempotencyKey: false,
            replayOfProcessingLogId: ownerId,
            cancellationToken: cancellationToken);
    }

    [Fact]
    public async Task ProcessingLogReplayForeignKeyAndOwnershipChecksAreEnforced()
    {
        await using var connection = await OpenWriteConnectionAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        await AssertSqlErrorAsync(
            547,
            () => SqlServerTestData.InsertProcessingLogAsync(
                connection,
                replayOfProcessingLogId: long.MaxValue,
                cancellationToken: cancellationToken));

        var ownerId = await SqlServerTestData.InsertProcessingLogAsync(
            connection,
            idempotencyKey: $"external:{Guid.NewGuid():N}",
            ownsIdempotencyKey: true,
            cancellationToken: cancellationToken);
        await AssertSqlErrorAsync(
            547,
            () => SqlServerTestData.InsertProcessingLogAsync(
                connection,
                idempotencyKey: $"external:{Guid.NewGuid():N}",
                ownsIdempotencyKey: true,
                replayOfProcessingLogId: ownerId,
                cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task ProcessingLogCategoricalAndNullableRangeChecksAreEnforced()
    {
        await using var connection = await OpenWriteConnectionAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        await AssertSqlErrorAsync(547, () => SqlServerTestData.InsertProcessingLogAsync(
            connection,
            eventType: "archive",
            cancellationToken: cancellationToken));
        await AssertSqlErrorAsync(547, () => SqlServerTestData.InsertProcessingLogAsync(
            connection,
            outcome: "Succeeded",
            cancellationToken: cancellationToken));
        await AssertSqlErrorAsync(547, () => SqlServerTestData.InsertProcessingLogAsync(
            connection,
            version: 0,
            cancellationToken: cancellationToken));
        await AssertSqlErrorAsync(547, () => SqlServerTestData.InsertProcessingLogAsync(
            connection,
            generation: -1,
            cancellationToken: cancellationToken));
        _ = await SqlServerTestData.InsertProcessingLogAsync(
            connection,
            generation: 0,
            cancellationToken: cancellationToken);
        await AssertSqlErrorAsync(547, () => SqlServerTestData.InsertProcessingLogAsync(
            connection,
            resultingVersion: 0,
            cancellationToken: cancellationToken));
    }

    private async Task<SqlConnection> OpenWriteConnectionAsync()
    {
        var connection = new SqlConnection(_fixture.WriteConnectionString);

        try
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static string UniqueId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private static async Task AssertSqlErrorAsync(int expectedErrorNumber, Func<Task> operation)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(operation);
        Assert.Equal(expectedErrorNumber, exception.Number);
    }
}
