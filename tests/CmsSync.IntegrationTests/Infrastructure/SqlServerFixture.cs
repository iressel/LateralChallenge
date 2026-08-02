using System.Security.Cryptography;
using CmsSync.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Xunit;

namespace CmsSync.IntegrationTests.Infrastructure;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private static readonly TimeSpan InitializationTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromMinutes(2);

    private readonly MsSqlContainer _container;
    private readonly string _readPassword;
    private bool _containerStarted;

    public SqlServerFixture()
    {
        var administratorPassword = CreateStrongPassword();
        _readPassword = CreateStrongPassword();
        DatabaseName = $"CmsSyncT008_{Guid.NewGuid():N}";
        ReadLoginName = $"CmsSyncRead_{Guid.NewGuid():N}";
        _container = new MsSqlBuilder(SqlServerTestConstants.Image)
            .WithPassword(administratorPassword)
            .Build();
    }

    public string DatabaseName { get; }

    public string ReadLoginName { get; }

    public string WriteConnectionString { get; private set; } = string.Empty;

    public string ReadConnectionString { get; private set; } = string.Empty;

    public int InitialApplicationTableCount { get; private set; }

    public string ProductVersion { get; private set; } = string.Empty;

    public string Edition { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        using var cancellationSource = new CancellationTokenSource(InitializationTimeout);
        var cancellationToken = cancellationSource.Token;

        await _container.StartAsync(cancellationToken);
        _containerStarted = true;

        var masterConnectionString = _container.GetConnectionString();
        await CreateApplicationDatabaseAsync(masterConnectionString, cancellationToken);

        WriteConnectionString = BuildConnectionString(masterConnectionString, DatabaseName);
        InitialApplicationTableCount = await CountApplicationTablesAsync(
            WriteConnectionString,
            cancellationToken);

        await ApplyMigrationAsync(cancellationToken);
        await VerifyMigrationHistoryAsync(cancellationToken);
        await CreateReadOnlyLoginAsync(masterConnectionString, cancellationToken);

        ReadConnectionString = BuildConnectionString(
            masterConnectionString,
            DatabaseName,
            ReadLoginName,
            _readPassword);

        (ProductVersion, Edition) = await ReadServerMetadataAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        using var cancellationSource = new CancellationTokenSource(CleanupTimeout);

        try
        {
            if (_containerStarted)
            {
                await DeleteDatabaseAndLoginAsync(cancellationSource.Token);
            }
        }
        finally
        {
            await _container.DisposeAsync();
        }
    }

    private static string CreateStrongPassword()
    {
        var randomText = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        return $"Aa9!{randomText}z";
    }

    private static string BuildConnectionString(
        string sourceConnectionString,
        string databaseName,
        string? userName = null,
        string? password = null)
    {
        var builder = new SqlConnectionStringBuilder(sourceConnectionString)
        {
            InitialCatalog = databaseName,
            PersistSecurityInfo = false,
        };

        if (userName is not null && password is not null)
        {
            builder.IntegratedSecurity = false;
            builder.UserID = userName;
            builder.Password = password;
        }

        return builder.ConnectionString;
    }

    private async Task CreateApplicationDatabaseAsync(
        string masterConnectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{DatabaseName}]";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CountApplicationTablesAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sys.tables WHERE [name] <> N'__EFMigrationsHistory'";

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task ApplyMigrationAsync(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<CmsWriteDbContext>()
            .UseSqlServer(WriteConnectionString)
            .Options;

        await using var context = new CmsWriteDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);
    }

    private async Task VerifyMigrationHistoryAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(WriteConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId]";

        var migrationIds = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            migrationIds.Add(reader.GetString(0));
        }

        if (migrationIds.Count != 1 ||
            !string.Equals(
                migrationIds[0],
                SqlServerTestConstants.MigrationId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The clean test database has an unexpected migration history.");
        }
    }

    private async Task CreateReadOnlyLoginAsync(
        string masterConnectionString,
        CancellationToken cancellationToken)
    {
        await using (var masterConnection = new SqlConnection(masterConnectionString))
        {
            await masterConnection.OpenAsync(cancellationToken);

            await using var createLoginCommand = masterConnection.CreateCommand();
            createLoginCommand.CommandText =
                $"CREATE LOGIN [{ReadLoginName}] WITH PASSWORD = '{_readPassword}', " +
                "CHECK_POLICY = ON, CHECK_EXPIRATION = OFF";
            await createLoginCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var applicationConnection = new SqlConnection(WriteConnectionString);
        await applicationConnection.OpenAsync(cancellationToken);

        await using var createUserCommand = applicationConnection.CreateCommand();
        createUserCommand.CommandText =
            $"CREATE USER [{ReadLoginName}] FOR LOGIN [{ReadLoginName}]; " +
            $"ALTER ROLE [db_datareader] ADD MEMBER [{ReadLoginName}];";
        await createUserCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<(string ProductVersion, string Edition)> ReadServerMetadataAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(WriteConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')), " +
            "CONVERT(nvarchar(128), SERVERPROPERTY('Edition'))";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("SQL Server did not return product metadata.");
        }

        return (reader.GetString(0), reader.GetString(1));
    }

    private async Task DeleteDatabaseAndLoginAsync(CancellationToken cancellationToken)
    {
        var masterConnectionString = _container.GetConnectionString();
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(N'{DatabaseName}') IS NOT NULL " +
            $"BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{DatabaseName}]; END; " +
            $"IF EXISTS (SELECT 1 FROM sys.server_principals WHERE [name] = N'{ReadLoginName}') " +
            $"DROP LOGIN [{ReadLoginName}];";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
