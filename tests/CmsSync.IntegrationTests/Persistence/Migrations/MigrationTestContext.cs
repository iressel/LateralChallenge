using CmsSync.Infrastructure.Persistence;
using CmsSync.IntegrationTests.Persistence.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CmsSync.IntegrationTests.Persistence.Migrations;

internal static class MigrationTestContext
{
    public static CmsWriteDbContext CreateWriteContext()
    {
        return PersistenceModelTestContext.CreateWriteContext();
    }

    public static CmsReadDbContext CreateReadContext()
    {
        return PersistenceModelTestContext.CreateReadContext();
    }

    public static IMigrationsAssembly GetMigrationsAssembly(DbContext context)
    {
        return context.GetService<IMigrationsAssembly>();
    }

    public static Migration CreateMigration(
        DbContext context,
        KeyValuePair<string, System.Reflection.TypeInfo> migrationEntry)
    {
        var migrationsAssembly = GetMigrationsAssembly(context);

        return migrationsAssembly.CreateMigration(
            migrationEntry.Value,
            context.Database.ProviderName
                ?? throw new InvalidOperationException("The migration test context has no database provider."));
    }

    public static string GenerateScript(MigrationsSqlGenerationOptions options)
    {
        using var context = CreateWriteContext();
        var migrator = context.GetService<IMigrator>();

        return migrator.GenerateScript(
            fromMigration: null,
            toMigration: null,
            options);
    }
}
