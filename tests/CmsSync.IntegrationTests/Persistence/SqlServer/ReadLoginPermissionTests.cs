using System.Data;
using CmsSync.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Xunit;

namespace CmsSync.IntegrationTests.Persistence.SqlServer;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "SqlServer")]
[Trait("Category", "Security")]
public sealed class ReadLoginPermissionTests
{
    private readonly SqlServerFixture _fixture;

    public ReadLoginPermissionTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ReadLoginCanSelectButCannotInsertUpdateDeleteOrModifySchema()
    {
        var entityId = $"read-permissions-{Guid.NewGuid():N}";
        await using (var writeConnection = new SqlConnection(_fixture.WriteConnectionString))
        {
            await writeConnection.OpenAsync(TestContext.Current.CancellationToken);
            await SqlServerTestData.InsertEntityAsync(
                writeConnection,
                entityId,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        await using var readConnection = new SqlConnection(_fixture.ReadConnectionString);
        await readConnection.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, await CountEntityAsync(readConnection, entityId));

        await AssertSqlErrorAsync(
            229,
            () => SqlServerTestData.InsertEntityAsync(
                readConnection,
                $"denied-insert-{Guid.NewGuid():N}",
                cancellationToken: TestContext.Current.CancellationToken));
        await AssertSqlErrorAsync(
            229,
            () => ExecuteEntityWriteAsync(
                readConnection,
                "UPDATE [CmsEntities] SET [UpdatedAtUtc] = SYSUTCDATETIME() WHERE [EntityId] = @entityId",
                entityId));
        await AssertSqlErrorAsync(
            229,
            () => ExecuteEntityWriteAsync(
                readConnection,
                "DELETE FROM [CmsEntities] WHERE [EntityId] = @entityId",
                entityId));
        await AssertSqlErrorAsync(
            262,
            () => ExecuteSchemaWriteAsync(readConnection));
    }

    [Fact]
    public async Task ReadLoginHasOnlyTheIntendedDatabaseRoleAndNoServerRole()
    {
        await using var connection = new SqlConnection(_fixture.ReadConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var databaseRoles = await ReadNamesAsync(
            connection,
            "SELECT rolePrincipal.[name] " +
            "FROM sys.database_role_members AS membership " +
            "INNER JOIN sys.database_principals AS rolePrincipal " +
            "ON rolePrincipal.[principal_id] = membership.[role_principal_id] " +
            "WHERE membership.[member_principal_id] = DATABASE_PRINCIPAL_ID() " +
            "ORDER BY rolePrincipal.[name]");
        var serverRoles = await ReadNamesAsync(
            connection,
            "SELECT rolePrincipal.[name] " +
            "FROM sys.server_role_members AS membership " +
            "INNER JOIN sys.server_principals AS rolePrincipal " +
            "ON rolePrincipal.[principal_id] = membership.[role_principal_id] " +
            "WHERE membership.[member_principal_id] = SUSER_ID() " +
            "ORDER BY rolePrincipal.[name]");

        Assert.Equal("db_datareader", Assert.Single(databaseRoles));
        Assert.DoesNotContain("db_owner", databaseRoles, StringComparer.Ordinal);
        Assert.DoesNotContain("db_datawriter", databaseRoles, StringComparer.Ordinal);
        Assert.DoesNotContain("db_ddladmin", databaseRoles, StringComparer.Ordinal);
        Assert.Empty(serverRoles);
    }

    private static async Task<int> CountEntityAsync(SqlConnection connection, string entityId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [CmsEntities] WHERE [EntityId] = @entityId";
        command.Parameters.Add("@entityId", SqlDbType.NVarChar, 200).Value = entityId;
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteEntityWriteAsync(
        SqlConnection connection,
        string commandText,
        string entityId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.Add("@entityId", SqlDbType.NVarChar, 200).Value = entityId;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task ExecuteSchemaWriteAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE [DeniedSchemaWrite_{Guid.NewGuid():N}] ([Id] int NOT NULL)";
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<string[]> ReadNamesAsync(SqlConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var names = new List<string>();

        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names.ToArray();
    }

    private static async Task AssertSqlErrorAsync(int expectedErrorNumber, Func<Task> operation)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(operation);
        Assert.Equal(expectedErrorNumber, exception.Number);
    }
}
