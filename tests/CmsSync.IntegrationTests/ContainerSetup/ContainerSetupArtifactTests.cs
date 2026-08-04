using CmsSync.IntegrationTests.Infrastructure;
using Xunit;

namespace CmsSync.IntegrationTests.ContainerSetup;

[Trait("Category", "ContainerSetup")]
public sealed class ContainerSetupArtifactTests
{
    private const string ExpectedSqlServerImage =
        "mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:" +
        "ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89";

    private static readonly string RepositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);

    [Fact]
    public void ComposeUsesTheExactImmutableSqlServerImage()
    {
        var compose = ReadRepositoryFile("compose.yaml");

        Assert.Equal(ExpectedSqlServerImage, SqlServerTestConstants.Image);
        Assert.Contains(ExpectedSqlServerImage, compose, StringComparison.Ordinal);
        Assert.DoesNotContain("2022-latest", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mssql/server:latest", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("platform:", compose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqlServiceUsesRuntimeSecretsPersistentStorageAndABoundedHealthCheck()
    {
        var compose = ReadRepositoryFile("compose.yaml");

        Assert.Contains("ACCEPT_EULA: \"Y\"", compose, StringComparison.Ordinal);
        Assert.Contains("MSSQL_PID: \"Developer\"", compose, StringComparison.Ordinal);
        Assert.Contains("MSSQL_SA_PASSWORD is required", compose, StringComparison.Ordinal);
        Assert.Contains("cms-sync-sql-data:/var/opt/mssql", compose, StringComparison.Ordinal);
        Assert.Contains("/opt/mssql-tools18/bin/sqlcmd", compose, StringComparison.Ordinal);
        Assert.Contains("retries: 30", compose, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:${SQL_SERVER_PORT:-14333}:1433", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("privileged:", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\n      SA_PASSWORD:", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeDefinesTheRequiredSuccessfulDependencyChain()
    {
        var compose = ReadRepositoryFile("compose.yaml");

        Assert.Contains("db-init:", compose, StringComparison.Ordinal);
        Assert.Contains("condition: service_healthy", compose, StringComparison.Ordinal);
        Assert.Contains("migration:", compose, StringComparison.Ordinal);
        Assert.Contains("condition: service_completed_successfully", compose, StringComparison.Ordinal);
        Assert.Contains("target: migration", compose, StringComparison.Ordinal);
        Assert.Contains("target: api", compose, StringComparison.Ordinal);
        Assert.Contains("GET /health/ready", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeRequiresEveryCredentialAndUsesTheCurrentAuthenticationHierarchy()
    {
        var compose = ReadRepositoryFile("compose.yaml");
        var requiredVariables = new[]
        {
            "MSSQL_SA_PASSWORD",
            "MIGRATION_SQL_PASSWORD",
            "WRITE_SQL_PASSWORD",
            "READ_SQL_PASSWORD",
            "Authentication__Credentials__Cms__Username",
            "Authentication__Credentials__Cms__Password",
            "Authentication__Credentials__Consumer__Username",
            "Authentication__Credentials__Consumer__Password",
            "Authentication__Credentials__Administrator__Username",
            "Authentication__Credentials__Administrator__Password",
        };

        foreach (var variable in requiredVariables)
        {
            Assert.Contains($"${{{variable}:?", compose, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Authentication__Cms", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("Authentication__NormalConsumer", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentExampleContainsOnlyPlaceholdersAndSafeDefaults()
    {
        var environmentExample = ReadRepositoryFile(".env.example");
        var credentialLines = environmentExample
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Contains("PASSWORD=", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(credentialLines);
        Assert.All(credentialLines, line => Assert.Contains("=<", line, StringComparison.Ordinal));
        Assert.Contains("Authentication__Credentials__Cms__Username", environmentExample, StringComparison.Ordinal);
        Assert.Contains("Authentication__Credentials__Consumer__Username", environmentExample, StringComparison.Ordinal);
        Assert.Contains("Authentication__Credentials__Administrator__Username", environmentExample, StringComparison.Ordinal);
        Assert.DoesNotContain("Authentication__Cms", environmentExample, StringComparison.Ordinal);
        Assert.DoesNotContain("Authentication__NormalConsumer", environmentExample, StringComparison.Ordinal);
    }

    [Fact]
    public void DockerfileHasSeparatePinnedMigrationAndNonRootApiTargets()
    {
        var dockerfile = ReadRepositoryFile("Dockerfile");

        Assert.Contains("sdk:10.0.302@sha256:", dockerfile, StringComparison.Ordinal);
        Assert.Contains("aspnet:10.0.10@sha256:", dockerfile, StringComparison.Ordinal);
        Assert.Contains("FROM ${SQL_SERVER_IMAGE} AS migration", dockerfile, StringComparison.Ordinal);
        Assert.Contains("AS api", dockerfile, StringComparison.Ordinal);
        Assert.Contains("--source https://api.nuget.org/v3/index.json", dockerfile, StringComparison.Ordinal);
        Assert.Contains("dotnet tool restore", dockerfile, StringComparison.Ordinal);
        Assert.Contains("USER app", dockerfile, StringComparison.Ordinal);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"CmsSync.Api.dll\"]", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void DockerIgnoreExcludesBuildLocalAndSecretState()
    {
        var dockerIgnore = ReadRepositoryFile(".dockerignore");
        var requiredEntries = new[]
        {
            ".git",
            ".vs",
            ".vscode",
            "**/bin",
            "**/obj",
            "TestResults",
            "coverage",
            "logs",
            "artifacts",
            ".env",
            ".env.*",
            "**/appsettings.Local.json",
            "**/*.pfx",
            "**/*.key",
            "**/*.mdf",
            ".docker",
        };

        Assert.All(requiredEntries, entry => Assert.Contains(entry, dockerIgnore, StringComparison.Ordinal));
        Assert.DoesNotContain("*.sln", dockerIgnore, StringComparison.Ordinal);
        Assert.DoesNotContain("*.csproj", dockerIgnore, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Build.props", dockerIgnore, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Packages.props", dockerIgnore, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet-tools.json", dockerIgnore, StringComparison.Ordinal);
    }

    [Fact]
    public void InitializationCreatesDistinctPrincipalsWithSchemaScopedPermissions()
    {
        var initialization = ReadRepositoryFile("scripts/container/initialize-database.sql");

        Assert.Contains("CREATE DATABASE [CmsSync]", initialization, StringComparison.Ordinal);
        Assert.Contains("CREATE LOGIN [CmsSyncMigration]", initialization, StringComparison.Ordinal);
        Assert.Contains("CREATE LOGIN [CmsSyncWriter]", initialization, StringComparison.Ordinal);
        Assert.Contains("CREATE LOGIN [CmsSyncReader]", initialization, StringComparison.Ordinal);
        Assert.Contains("GRANT CONTROL ON SCHEMA::[dbo] TO [CmsSyncMigration]", initialization, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[dbo] TO [CmsSyncWriter]", initialization, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT ON SCHEMA::[dbo] TO [CmsSyncReader]", initialization, StringComparison.Ordinal);
        Assert.Contains("DENY INSERT, UPDATE, DELETE ON SCHEMA::[dbo] TO [CmsSyncReader]", initialization, StringComparison.Ordinal);
        Assert.DoesNotContain("db_owner", initialization, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("db_datawriter", initialization, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationTargetUsesTheRepositoryToolAndApiDoesNotAutoMigrate()
    {
        var migrationScript = ReadRepositoryFile("scripts/container/apply-migrations.sh");
        var dockerfile = ReadRepositoryFile("Dockerfile");
        var toolManifest = ReadRepositoryFile(".config/dotnet-tools.json");
        var apiSource = Directory
            .GetFiles(Path.Combine(RepositoryRoot, "src", "CmsSync.Api"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.Contains("\"version\": \"10.0.10\"", toolManifest, StringComparison.Ordinal);
        Assert.Contains("dotnet ef migrations script", dockerfile, StringComparison.Ordinal);
        Assert.Contains("--idempotent", dockerfile, StringComparison.Ordinal);
        Assert.Contains("--context CmsWriteDbContext", dockerfile, StringComparison.Ordinal);
        Assert.Contains("-U CmsSyncMigration", migrationScript, StringComparison.Ordinal);
        Assert.Contains("    -I \\", migrationScript, StringComparison.Ordinal);
        Assert.Contains("/opt/cms-sync/migrations.sql", migrationScript, StringComparison.Ordinal);
        Assert.DoesNotContain("--connection", migrationScript, StringComparison.Ordinal);
        Assert.DoesNotContain(apiSource, source => source.Contains(".Migrate(", StringComparison.Ordinal));
        Assert.DoesNotContain(apiSource, source => source.Contains(".MigrateAsync(", StringComparison.Ordinal));
        Assert.DoesNotContain(apiSource, source => source.Contains("EnsureCreated", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidationScriptExercisesTheRequiredCleanVolumeSmokeAndCleanup()
    {
        var validationScript = ReadRepositoryFile("scripts/validate-container-setup.ps1");

        Assert.Contains(ExpectedSqlServerImage, validationScript, StringComparison.Ordinal);
        Assert.Contains("docker", validationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("compose", validationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("config", validationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--quiet", validationScript, StringComparison.Ordinal);
        Assert.Contains("--build", validationScript, StringComparison.Ordinal);
        Assert.Contains("--wait", validationScript, StringComparison.Ordinal);
        Assert.Contains("/health/live", validationScript, StringComparison.Ordinal);
        Assert.Contains("/health/ready", validationScript, StringComparison.Ordinal);
        Assert.Contains(SqlServerTestConstants.MigrationId, validationScript, StringComparison.Ordinal);
        Assert.Contains("verify-read-only.sh", validationScript, StringComparison.Ordinal);
        Assert.Contains("/cms/events", validationScript, StringComparison.Ordinal);
        Assert.Contains("down", validationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--volumes", validationScript, StringComparison.Ordinal);
        Assert.Contains("--remove-orphans", validationScript, StringComparison.Ordinal);
        Assert.Contains("finally", validationScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppleSiliconGuidanceUsesRemoteSqlWithoutEmulationClaims()
    {
        var documentation = ReadRepositoryFile("docs/container-development.md");

        Assert.Contains("Apple Silicon", documentation, StringComparison.Ordinal);
        Assert.Contains("remote supported SQL Server instance or Azure SQL", documentation, StringComparison.Ordinal);
        Assert.Contains("not supported through Rosetta, QEMU", documentation, StringComparison.Ordinal);
        Assert.Contains("Do not add `platform: linux/amd64`", documentation, StringComparison.Ordinal);
        Assert.Contains("SELECT-only principal", documentation, StringComparison.Ordinal);
        Assert.Contains("migration-capable remote connection", documentation, StringComparison.Ordinal);
        Assert.Contains("SQL Server Testcontainers are not the local verification path", documentation, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionGuidanceKeepsMigrationWriteAndReadPrincipalsDistinct()
    {
        var documentation = ReadRepositoryFile("docs/container-development.md");
        var compose = ReadRepositoryFile("compose.yaml");

        Assert.Contains("migration, write, and read credentials independently", documentation, StringComparison.Ordinal);
        Assert.Contains("MIGRATION_SQL_PASSWORD is required", compose, StringComparison.Ordinal);
        Assert.Contains("CmsSyncMigration", ReadRepositoryFile("scripts/container/apply-migrations.sh"), StringComparison.Ordinal);
        Assert.Contains("User ID=CmsSyncWriter", compose, StringComparison.Ordinal);
        Assert.Contains("User ID=CmsSyncReader", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("User ID=sa", compose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContainerAndProductionArtifactsHaveNoLocalDbDependency()
    {
        var inspectedFiles = Directory
            .GetFiles(Path.Combine(RepositoryRoot, "src"), "*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Append(Path.Combine(RepositoryRoot, "compose.yaml"))
            .Append(Path.Combine(RepositoryRoot, "Dockerfile"));

        Assert.All(
            inspectedFiles,
            path => Assert.DoesNotContain(
                "LocalDB",
                File.ReadAllText(path),
                StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot, relativePath));
    }

    private static string FindRepositoryRoot(string startingPath)
    {
        for (var directory = new DirectoryInfo(startingPath); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LateralChallenge.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the LateralChallenge repository root.");
    }
}
