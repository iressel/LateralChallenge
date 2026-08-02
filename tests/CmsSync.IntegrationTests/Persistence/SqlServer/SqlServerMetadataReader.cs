using Microsoft.Data.SqlClient;

namespace CmsSync.IntegrationTests.Persistence.SqlServer;

internal static class SqlServerMetadataReader
{
    public static async Task<string[]> ReadApplicationTablesAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        const string sql =
            "SELECT [name] FROM sys.tables " +
            "WHERE [is_ms_shipped] = 0 AND [name] <> N'__EFMigrationsHistory' ORDER BY [name]";

        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tables = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables.ToArray();
    }

    public static async Task<List<(
        string TableName,
        string ColumnName,
        string TypeName,
        short MaximumLength,
        byte Precision,
        byte Scale,
        bool IsNullable,
        bool IsIdentity,
        string? Collation)>> ReadColumnsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        const string sql =
            "SELECT t.[name], c.[name], ty.[name], c.[max_length], c.[precision], c.[scale], " +
            "c.[is_nullable], c.[is_identity], c.[collation_name] " +
            "FROM sys.tables AS t " +
            "INNER JOIN sys.columns AS c ON c.[object_id] = t.[object_id] " +
            "INNER JOIN sys.types AS ty ON ty.[user_type_id] = c.[user_type_id] " +
            "WHERE t.[is_ms_shipped] = 0 AND t.[name] <> N'__EFMigrationsHistory' " +
            "ORDER BY t.[name], c.[column_id]";

        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<(
            string,
            string,
            string,
            short,
            byte,
            byte,
            bool,
            bool,
            string?)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt16(3),
                reader.GetByte(4),
                reader.GetByte(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return columns;
    }

    public static async Task<List<(
        string TableName,
        string ConstraintName,
        string Definition)>> ReadCheckConstraintsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        const string sql =
            "SELECT t.[name], cc.[name], cc.[definition] " +
            "FROM sys.check_constraints AS cc " +
            "INNER JOIN sys.tables AS t ON t.[object_id] = cc.[parent_object_id] " +
            "WHERE t.[is_ms_shipped] = 0 ORDER BY t.[name], cc.[name]";

        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var constraints = new List<(string, string, string)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            constraints.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return constraints;
    }

    public static async Task<List<(
        string TableName,
        string IndexName,
        bool IsUnique,
        bool IsPrimaryKey,
        bool IsUniqueConstraint,
        string? FilterDefinition,
        int KeyOrdinal,
        string ColumnName)>> ReadIndexColumnsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        const string sql =
            "SELECT t.[name], i.[name], i.[is_unique], i.[is_primary_key], i.[is_unique_constraint], " +
            "i.[filter_definition], ic.[key_ordinal], c.[name] " +
            "FROM sys.tables AS t " +
            "INNER JOIN sys.indexes AS i ON i.[object_id] = t.[object_id] " +
            "INNER JOIN sys.index_columns AS ic ON ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id] " +
            "INNER JOIN sys.columns AS c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id] " +
            "WHERE t.[is_ms_shipped] = 0 AND t.[name] <> N'__EFMigrationsHistory' AND i.[index_id] > 0 " +
            "ORDER BY t.[name], i.[name], ic.[key_ordinal]";

        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var indexes = new List<(string, string, bool, bool, bool, string?, int, string)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            indexes.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetByte(6),
                reader.GetString(7)));
        }

        return indexes;
    }

    public static async Task<List<(
        string TableName,
        string ConstraintName,
        int KeyOrdinal,
        string ColumnName)>> ReadPrimaryKeyColumnsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        const string sql =
            "SELECT t.[name], kc.[name], ic.[key_ordinal], c.[name] " +
            "FROM sys.key_constraints AS kc " +
            "INNER JOIN sys.tables AS t ON t.[object_id] = kc.[parent_object_id] " +
            "INNER JOIN sys.index_columns AS ic ON ic.[object_id] = kc.[parent_object_id] " +
            "AND ic.[index_id] = kc.[unique_index_id] " +
            "INNER JOIN sys.columns AS c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id] " +
            "WHERE kc.[type] = 'PK' AND t.[name] <> N'__EFMigrationsHistory' " +
            "ORDER BY t.[name], ic.[key_ordinal]";

        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var primaryKeys = new List<(string, string, int, string)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            primaryKeys.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetByte(2),
                reader.GetString(3)));
        }

        return primaryKeys;
    }

    public static async Task<List<(
        string ForeignKeyName,
        string ParentTable,
        string ParentColumn,
        string ReferencedTable,
        string ReferencedColumn,
        string DeleteAction)>> ReadForeignKeysAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        const string sql =
            "SELECT fk.[name], pt.[name], pc.[name], rt.[name], rc.[name], fk.[delete_referential_action_desc] " +
            "FROM sys.foreign_keys AS fk " +
            "INNER JOIN sys.foreign_key_columns AS fkc ON fkc.[constraint_object_id] = fk.[object_id] " +
            "INNER JOIN sys.tables AS pt ON pt.[object_id] = fk.[parent_object_id] " +
            "INNER JOIN sys.columns AS pc ON pc.[object_id] = pt.[object_id] AND pc.[column_id] = fkc.[parent_column_id] " +
            "INNER JOIN sys.tables AS rt ON rt.[object_id] = fk.[referenced_object_id] " +
            "INNER JOIN sys.columns AS rc ON rc.[object_id] = rt.[object_id] AND rc.[column_id] = fkc.[referenced_column_id] " +
            "ORDER BY fk.[name], fkc.[constraint_column_id]";

        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var foreignKeys = new List<(string, string, string, string, string, string)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            foreignKeys.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        return foreignKeys;
    }

    private static async Task<SqlConnection> OpenConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static SqlCommand CreateCommand(SqlConnection connection, string commandText)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        return command;
    }
}
