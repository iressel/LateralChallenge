using System.Data;
using Microsoft.Data.SqlClient;

namespace CmsSync.IntegrationTests.Persistence.SqlServer;

internal static class SqlServerTestData
{
    private static readonly DateTime DefaultTimestampUtc =
        new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    public static async Task InsertEntityAsync(
        SqlConnection connection,
        string entityId,
        long generation = 1,
        long latestVersion = 1,
        string payload = "{\"value\":1}",
        string publicationStatus = "Published",
        DateTime? currentVersionOccurredAtUtc = null,
        DateTime? entityEventHighWatermarkUtc = null,
        bool administrativeDisabled = false,
        DateTime? administrativeStateChangedAtUtc = null,
        string? administrativeStateChangedBy = null,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO [CmsEntities] " +
            "([EntityId], [Generation], [LatestVersion], [Payload], [PayloadHash], " +
            "[CmsPublicationStatus], [CurrentVersionOccurredAtUtc], [EntityEventHighWatermarkUtc], " +
            "[AdministrativeDisabled], [AdministrativeStateChangedAtUtc], [AdministrativeStateChangedBy], " +
            "[CreatedAtUtc], [UpdatedAtUtc]) VALUES " +
            "(@entityId, @generation, @latestVersion, @payload, @payloadHash, @publicationStatus, " +
            "@currentTimestamp, @highWatermark, @administrativeDisabled, @administrativeTimestamp, " +
            "@administrativeSubject, @createdAtUtc, @updatedAtUtc)";

        var currentTimestamp = currentVersionOccurredAtUtc ?? DefaultTimestampUtc;
        var highWatermark = entityEventHighWatermarkUtc ?? currentTimestamp;
        AddNVarChar(command, "@entityId", 200, entityId);
        command.Parameters.Add("@generation", SqlDbType.BigInt).Value = generation;
        command.Parameters.Add("@latestVersion", SqlDbType.BigInt).Value = latestVersion;
        AddNVarChar(command, "@payload", -1, payload);
        command.Parameters.Add("@payloadHash", SqlDbType.Binary, 32).Value = CreateHash(1);
        AddVarChar(command, "@publicationStatus", 16, publicationStatus);
        AddDateTime(command, "@currentTimestamp", currentTimestamp);
        AddDateTime(command, "@highWatermark", highWatermark);
        command.Parameters.Add("@administrativeDisabled", SqlDbType.Bit).Value = administrativeDisabled;
        AddNullableDateTime(command, "@administrativeTimestamp", administrativeStateChangedAtUtc);
        AddNullableNVarChar(command, "@administrativeSubject", 200, administrativeStateChangedBy);
        AddDateTime(command, "@createdAtUtc", DefaultTimestampUtc);
        AddDateTime(command, "@updatedAtUtc", DefaultTimestampUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task InsertRevisionAsync(
        SqlConnection connection,
        string entityId,
        long generation = 1,
        long version = 1,
        string payload = "{\"value\":1}",
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO [CmsEntityRevisions] " +
            "([EntityId], [Generation], [Version], [FirstObservedPayload], [PayloadHash], [FirstObservedAtUtc]) " +
            "VALUES (@entityId, @generation, @version, @payload, @payloadHash, @observedAtUtc)";

        AddNVarChar(command, "@entityId", 200, entityId);
        command.Parameters.Add("@generation", SqlDbType.BigInt).Value = generation;
        command.Parameters.Add("@version", SqlDbType.BigInt).Value = version;
        AddNVarChar(command, "@payload", -1, payload);
        command.Parameters.Add("@payloadHash", SqlDbType.Binary, 32).Value = CreateHash(2);
        AddDateTime(command, "@observedAtUtc", DefaultTimestampUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task InsertTombstoneAsync(
        SqlConnection connection,
        string entityId,
        long generation,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO [CmsDeletionTombstones] " +
            "([EntityId], [LastDeletedGeneration], [DeletedAtUtc], [LastDeleteEventKey], " +
            "[CreatedAtUtc], [UpdatedAtUtc]) " +
            "VALUES (@entityId, @generation, @deletedAtUtc, NULL, @createdAtUtc, @updatedAtUtc)";

        AddNVarChar(command, "@entityId", 200, entityId);
        command.Parameters.Add("@generation", SqlDbType.BigInt).Value = generation;
        AddDateTime(command, "@deletedAtUtc", DefaultTimestampUtc);
        AddDateTime(command, "@createdAtUtc", DefaultTimestampUtc);
        AddDateTime(command, "@updatedAtUtc", DefaultTimestampUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<long> InsertProcessingLogAsync(
        SqlConnection connection,
        Guid? batchId = null,
        int sequence = 0,
        string? idempotencyKey = null,
        bool ownsIdempotencyKey = false,
        long? replayOfProcessingLogId = null,
        string? eventType = "publish",
        string outcome = "Applied",
        long? version = 1,
        long? generation = 1,
        long? resultingVersion = 1,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO [CmsEventProcessingLogs] " +
            "([BatchId], [Sequence], [IdempotencyKey], [OwnsIdempotencyKey], " +
            "[ReplayOfProcessingLogId], [ExternalEventId], [EventContentHash], [PayloadHash], " +
            "[EventType], [EntityId], [Version], [EventOccurredAtUtc], [Outcome], [Code], " +
            "[Generation], [ResultingVersion], [ProcessedAtUtc], [CorrelationId], [AuthenticatedCmsSubject]) " +
            "OUTPUT INSERTED.[ProcessingLogId] VALUES " +
            "(@batchId, @sequence, @idempotencyKey, @ownsIdempotencyKey, @replayOfProcessingLogId, " +
            "NULL, NULL, NULL, @eventType, @entityId, @version, @eventOccurredAtUtc, @outcome, " +
            "@code, @generation, @resultingVersion, @processedAtUtc, @correlationId, @subject)";

        command.Parameters.Add("@batchId", SqlDbType.UniqueIdentifier).Value = batchId ?? Guid.NewGuid();
        command.Parameters.Add("@sequence", SqlDbType.Int).Value = sequence;
        AddNullableNVarChar(command, "@idempotencyKey", 209, idempotencyKey);
        command.Parameters.Add("@ownsIdempotencyKey", SqlDbType.Bit).Value = ownsIdempotencyKey;
        command.Parameters.Add("@replayOfProcessingLogId", SqlDbType.BigInt).Value =
            replayOfProcessingLogId.HasValue ? replayOfProcessingLogId.Value : DBNull.Value;
        AddNullableVarChar(command, "@eventType", 16, eventType);
        AddNVarChar(command, "@entityId", 200, $"entity-{Guid.NewGuid():N}");
        command.Parameters.Add("@version", SqlDbType.BigInt).Value = version.HasValue ? version.Value : DBNull.Value;
        AddDateTime(command, "@eventOccurredAtUtc", DefaultTimestampUtc);
        AddVarChar(command, "@outcome", 16, outcome);
        AddVarChar(command, "@code", 100, "T008_TEST");
        command.Parameters.Add("@generation", SqlDbType.BigInt).Value =
            generation.HasValue ? generation.Value : DBNull.Value;
        command.Parameters.Add("@resultingVersion", SqlDbType.BigInt).Value =
            resultingVersion.HasValue ? resultingVersion.Value : DBNull.Value;
        AddDateTime(command, "@processedAtUtc", DefaultTimestampUtc);
        AddNVarChar(command, "@correlationId", 200, Guid.NewGuid().ToString("N"));
        AddNVarChar(command, "@subject", 200, "t008-integration-test");

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] CreateHash(byte value)
    {
        var hash = new byte[32];
        Array.Fill(hash, value);
        return hash;
    }

    private static void AddNVarChar(SqlCommand command, string name, int size, string value)
    {
        command.Parameters.Add(name, SqlDbType.NVarChar, size).Value = value;
    }

    private static void AddNullableNVarChar(
        SqlCommand command,
        string name,
        int size,
        string? value)
    {
        command.Parameters.Add(name, SqlDbType.NVarChar, size).Value = value ?? (object)DBNull.Value;
    }

    private static void AddVarChar(SqlCommand command, string name, int size, string value)
    {
        command.Parameters.Add(name, SqlDbType.VarChar, size).Value = value;
    }

    private static void AddNullableVarChar(
        SqlCommand command,
        string name,
        int size,
        string? value)
    {
        command.Parameters.Add(name, SqlDbType.VarChar, size).Value = value ?? (object)DBNull.Value;
    }

    private static void AddDateTime(SqlCommand command, string name, DateTime value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.DateTime2);
        parameter.Scale = 7;
        parameter.Value = value;
    }

    private static void AddNullableDateTime(SqlCommand command, string name, DateTime? value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.DateTime2);
        parameter.Scale = 7;
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }
}
