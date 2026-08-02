using System.Data;
using CmsSync.Infrastructure.Persistence;
using CmsSync.IntegrationTests.Infrastructure;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.Persistence.SqlServer;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "SqlServer")]
public sealed class DbContextRegistrationTests
{
    private readonly SqlServerFixture _fixture;

    public DbContextRegistrationTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProductionCompositionRegistersScopedSqlServerReadAndWriteContexts()
    {
        await using var factory = new CmsSyncWebApplicationFactory(
            _fixture.WriteConnectionString,
            _fixture.ReadConnectionString);
        using var firstScope = factory.Services.CreateScope();
        var firstWrite = firstScope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var firstRead = firstScope.ServiceProvider.GetRequiredService<CmsReadDbContext>();
        var sameWrite = firstScope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var sameRead = firstScope.ServiceProvider.GetRequiredService<CmsReadDbContext>();

        using var secondScope = factory.Services.CreateScope();
        var secondWrite = secondScope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        var secondRead = secondScope.ServiceProvider.GetRequiredService<CmsReadDbContext>();

        Assert.Same(firstWrite, sameWrite);
        Assert.Same(firstRead, sameRead);
        Assert.NotSame(firstWrite, secondWrite);
        Assert.NotSame(firstRead, secondRead);
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", firstWrite.Database.ProviderName);
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", firstRead.Database.ProviderName);
        AssertConnectionIdentity(_fixture.WriteConnectionString, firstWrite.Database.GetConnectionString());
        AssertConnectionIdentity(_fixture.ReadConnectionString, firstRead.Database.GetConnectionString());
        Assert.Equal(ConnectionState.Closed, firstWrite.Database.GetDbConnection().State);
        Assert.Equal(ConnectionState.Closed, firstRead.Database.GetDbConnection().State);

        var appliedMigrations = await firstWrite.Database
            .GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[] { SqlServerTestConstants.MigrationId }, appliedMigrations);
        Assert.Empty(firstRead.Database.GetMigrations());
    }

    [Fact]
    public async Task ProgramStartupLeavesMigrationHistoryUnchanged()
    {
        var before = await CountMigrationRowsAsync();

        await using (var factory = new CmsSyncWebApplicationFactory(
                         _fixture.WriteConnectionString,
                         _fixture.ReadConnectionString))
        {
            using var scope = factory.Services.CreateScope();
            var writeContext = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
            var readContext = scope.ServiceProvider.GetRequiredService<CmsReadDbContext>();

            Assert.Equal(ConnectionState.Closed, writeContext.Database.GetDbConnection().State);
            Assert.Equal(ConnectionState.Closed, readContext.Database.GetDbConnection().State);
        }

        var after = await CountMigrationRowsAsync();
        Assert.Equal(before, after);
        Assert.Equal(1, after);
    }

    [Fact]
    public async Task ProgramFailsSafelyWhenARequiredConnectionStringIsMissing()
    {
        const string safeConnectionString =
            "Server=configuration-only.invalid;Database=configuration-only;Integrated Security=true";

        await AssertMissingConnectionStringAsync(
            writeConnectionString: null,
            readConnectionString: safeConnectionString,
            "ConnectionStrings:WriteDatabase is required.");
        await AssertMissingConnectionStringAsync(
            writeConnectionString: safeConnectionString,
            readConnectionString: null,
            "ConnectionStrings:ReadDatabase is required.");
    }

    private async Task<int> CountMigrationRowsAsync()
    {
        await using var connection = new SqlConnection(_fixture.WriteConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [__EFMigrationsHistory]";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AssertConnectionIdentity(string expected, string? actual)
    {
        Assert.NotNull(actual);
        var expectedBuilder = new SqlConnectionStringBuilder(expected);
        var actualBuilder = new SqlConnectionStringBuilder(actual);

        Assert.Equal(expectedBuilder.DataSource, actualBuilder.DataSource);
        Assert.Equal(expectedBuilder.InitialCatalog, actualBuilder.InitialCatalog);
        Assert.Equal(expectedBuilder.UserID, actualBuilder.UserID);
        Assert.Equal(expectedBuilder.IntegratedSecurity, actualBuilder.IntegratedSecurity);
    }

    private static async Task AssertMissingConnectionStringAsync(
        string? writeConnectionString,
        string? readConnectionString,
        string expectedMessage)
    {
        await using var factory = new CmsSyncWebApplicationFactory(
            writeConnectionString,
            readConnectionString);

        var exception = Assert.Throws<InvalidOperationException>(() => _ = factory.Services);
        var messages = ReadExceptionMessages(exception);

        Assert.Contains(expectedMessage, messages, StringComparison.Ordinal);
        Assert.DoesNotContain("configuration-only.invalid", messages, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", messages, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("User ID=", messages, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadExceptionMessages(Exception exception)
    {
        var messages = new List<string>();

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(Environment.NewLine, messages);
    }
}
