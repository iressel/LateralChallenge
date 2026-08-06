using Xunit;

namespace CmsSync.IntegrationTests.Aspire;

[Trait("Category", "Aspire")]
public sealed class AspireArtifactTests
{
    private static readonly string RepositoryRoot = ResolveRepositoryRoot();

    [Fact]
    public void AppHostUsesPinnedAspirePackagesAndExpectedResources()
    {
        var appHostPath = Path.Combine(RepositoryRoot, "apphost.cs");
        Assert.True(File.Exists(appHostPath), $"Missing file: {appHostPath}");

        var content = File.ReadAllText(appHostPath);

        Assert.Contains("#:sdk Aspire.AppHost.Sdk@13.4.0", content, StringComparison.Ordinal);
        Assert.Contains("#:package Aspire.Hosting.SqlServer@13.4.0", content, StringComparison.Ordinal);
        Assert.Contains("AddSqlServer(\"sql\"", content, StringComparison.Ordinal);
        Assert.Contains("AddContainer(\"db-init\"", content, StringComparison.Ordinal);
        Assert.Contains("AddDockerfile(\"migration\"", content, StringComparison.Ordinal);
        Assert.Contains("AddProject(\"api\", \"src/CmsSync.Api/CmsSync.Api.csproj\")", content, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostDefinesExpectedDependencyChain()
    {
        var appHostPath = Path.Combine(RepositoryRoot, "apphost.cs");
        var content = File.ReadAllText(appHostPath);

        Assert.Contains(".WaitFor(sql)", content, StringComparison.Ordinal);
        Assert.Contains(".WaitForCompletion(dbInit)", content, StringComparison.Ordinal);
        Assert.Contains(".WaitForCompletion(migration)", content, StringComparison.Ordinal);
        Assert.Contains(".WithLifetime(ContainerLifetime.Persistent)", content, StringComparison.Ordinal);
        Assert.Contains(".WithHttpHealthCheck(\"/health/ready\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostUsesPinnedSqlServerImageAndLoopbackHostBinding()
    {
        var appHostPath = Path.Combine(RepositoryRoot, "apphost.cs");
        var content = File.ReadAllText(appHostPath);

        Assert.Contains("2022-CU26-ubuntu-22.04@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89", content, StringComparison.Ordinal);
        Assert.Contains("const string SqlServerHostAddress = \"127.0.0.1\";", content, StringComparison.Ordinal);
        Assert.Contains("const int SqlServerHostPort = 14333;", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigureAspireLocalScriptExistsAndSetsRequiredParameters()
    {
        var scriptPath = Path.Combine(RepositoryRoot, "scripts", "configure-aspire-local.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing file: {scriptPath}");

        var content = File.ReadAllText(scriptPath);

        Assert.Contains("aspire --version", content, StringComparison.Ordinal);
        Assert.Contains("13.4.0", content, StringComparison.Ordinal);
        Assert.Contains("aspire secret set \"Parameters:$Name\"", content, StringComparison.Ordinal);
        Assert.Contains("-Name \"mssql-sa-password\"", content, StringComparison.Ordinal);
        Assert.Contains("-Name \"migration-sql-password\"", content, StringComparison.Ordinal);
        Assert.Contains("-Name \"write-sql-password\"", content, StringComparison.Ordinal);
        Assert.Contains("-Name \"read-sql-password\"", content, StringComparison.Ordinal);
        Assert.Contains("-Name \"cms-username\"", content, StringComparison.Ordinal);
        Assert.Contains("-Name \"cms-password\"", content, StringComparison.Ordinal);
        Assert.Contains("-Name \"consumer-username\"", content, StringComparison.Ordinal);
        Assert.Contains("-Name \"consumer-password\"", content, StringComparison.Ordinal);
        Assert.Contains("-Name \"administrator-username\"", content, StringComparison.Ordinal);
        Assert.Contains("-Name \"administrator-password\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateAspireSetupScriptExistsAndPerformsLifecycleChecks()
    {
        var scriptPath = Path.Combine(RepositoryRoot, "scripts", "validate-aspire-setup.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing file: {scriptPath}");

        var content = File.ReadAllText(scriptPath);

        Assert.Contains("\"start\"", content, StringComparison.Ordinal);
        Assert.Contains("--isolated", content, StringComparison.Ordinal);
        Assert.Contains("\"wait\"", content, StringComparison.Ordinal);
        Assert.Contains("api", content, StringComparison.Ordinal);
        Assert.Contains("db-init", content, StringComparison.Ordinal);
        Assert.Contains("migration", content, StringComparison.Ordinal);
        Assert.Contains("/health/live", content, StringComparison.Ordinal);
        Assert.Contains("/health/ready", content, StringComparison.Ordinal);
        Assert.Contains("/swagger/v1/swagger.json", content, StringComparison.Ordinal);
        Assert.Contains("Aspire__SqlDataVolumeName", content, StringComparison.Ordinal);
        Assert.Contains("Stop-AspireAppHost", content, StringComparison.Ordinal);
        Assert.Contains("Remove-ContainersByName", content, StringComparison.Ordinal);
        Assert.Contains("docker volume rm", content, StringComparison.Ordinal);
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var marker = Path.Combine(current.FullName, "LateralChallenge.sln");
            if (File.Exists(marker))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }
}
